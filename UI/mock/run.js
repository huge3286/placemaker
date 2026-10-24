// npm run mock     → build the mock UI + mock host (real Core), start it on a free port, print the URL.
// npm run test:ui  → same, then run the Playwright suite against it and stop the host.
// Flags: --no-host (serve without dotnet: the mock falls back to its fake planner), --port N.
const { spawn, spawnSync } = require("child_process");
const fs = require("fs");
const http = require("http");
const net = require("net");
const path = require("path");

const UI = path.join(__dirname, "..");
const HOST = path.join(__dirname, "host");
const DLL = path.join(HOST, "bin", "Debug", "net9.0", "Citymaker.MockHost.dll");
const args = process.argv.slice(2);
const testMode = args.includes("--test");
const npx = process.platform === "win32" ? "npx.cmd" : "npx";

function run(cmd, cmdArgs, opts = {}) {
    const r = spawnSync(cmd, cmdArgs, { stdio: "inherit", cwd: UI, shell: process.platform === "win32", ...opts });
    if (r.status !== 0) {
        console.error(`\n✖ ${cmd} ${cmdArgs.join(" ")} failed (${r.status})`);
        process.exit(r.status || 1);
    }
}

function freePort() {
    return new Promise((resolve, reject) => {
        const srv = net.createServer();
        srv.listen(0, "127.0.0.1", () => {
            const { port } = srv.address();
            srv.close(() => resolve(port));
        });
        srv.on("error", reject);
    });
}

function waitFor(url, ms) {
    const until = Date.now() + ms;
    return new Promise((resolve, reject) => {
        const tick = () => {
            http.get(url, (res) => {
                res.resume();
                if (res.statusCode === 200) resolve();
                else retry();
            }).on("error", retry);
        };
        const retry = () => (Date.now() > until ? reject(new Error("host did not start: " + url)) : setTimeout(tick, 200));
        tick();
    });
}

/** Minimal static server for --no-host. */
function staticServer(port) {
    const types = { ".html": "text/html", ".js": "text/javascript", ".css": "text/css", ".svg": "image/svg+xml", ".map": "application/json" };
    const srv = http.createServer((req, res) => {
        const url = decodeURIComponent((req.url || "/").split("?")[0]);
        if (url.startsWith("/api/")) {
            res.writeHead(404);
            return res.end();
        }
        const file = path.join(__dirname, url === "/" ? "index.html" : url);
        if (!file.startsWith(__dirname) || !fs.existsSync(file)) {
            res.writeHead(404);
            return res.end();
        }
        res.writeHead(200, { "Content-Type": types[path.extname(file)] || "application/octet-stream" });
        fs.createReadStream(file).pipe(res);
    });
    srv.listen(port, "127.0.0.1");
    return { kill: () => srv.close() };
}

(async () => {
    const noHost = args.includes("--no-host");
    const portArg = args.indexOf("--port");
    const port = portArg >= 0 ? Number(args[portArg + 1]) : await freePort();

    console.log("• Building the mock UI…");
    run("node", ["tools/extract-lang.js"]);
    run(npx, ["webpack", "--config", "mock/webpack.config.js"]);

    let server;
    if (noHost) {
        server = staticServer(port);
    } else {
        console.log("• Building the mock host (real Core)…");
        run("dotnet", ["build", HOST, "-nologo", "-v", "q", "-clp:ErrorsOnly"]);
        server = spawn("dotnet", [DLL, "--port", String(port), "--root", __dirname], { stdio: testMode ? "ignore" : "inherit" });
        server.on("exit", (code) => {
            if (!testMode && code) process.exit(code);
        });
    }
    const url = `http://127.0.0.1:${port}/`;
    await waitFor(url, 30000);
    console.log(`\n  Citymaker UI mock:  ${url}\n`);

    if (!testMode) {
        const stop = () => {
            server.kill();
            process.exit(0);
        };
        process.on("SIGINT", stop);
        process.on("SIGTERM", stop);
        return;
    }
    const pw = spawnSync(npx, ["playwright", "test", ...args.filter((a) => a !== "--test" && a !== "--no-host")], {
        stdio: "inherit",
        cwd: UI,
        shell: process.platform === "win32",
        env: { ...process.env, PM_MOCK_URL: url },
    });
    server.kill();
    process.exit(pw.status ?? 1);
})().catch((e) => {
    console.error(e);
    process.exit(1);
});
