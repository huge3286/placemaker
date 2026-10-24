const path = require("path");
const MOD = require("./mod.json");
const MiniCssExtractPlugin = require("mini-css-extract-plugin");
const { CSSPresencePlugin } = require("./tools/css-presence");
const { CssLintPlugin } = require("./tools/css-lint");
const TerserPlugin = require("terser-webpack-plugin");
const gray = (text) => `\x1b[90m${text}\x1b[0m`;

// Output: CITYMAKER_UI_OUT (set by the csproj) > ./build. A bare `npm run build` never writes into the game; deploying goes through the csproj.
const CSII_USERDATAPATH = process.env.CSII_USERDATAPATH;

const OUTPUT_DIR = process.env.CITYMAKER_UI_OUT || path.join(__dirname, "build");

const banner = `
 * Cities: Skylines II UI Module
 *
 * Id: ${MOD.id}
 * Author: ${MOD.author}
 * Version: ${MOD.version}
 * Dependencies: ${MOD.dependencies.join(",")}
`;

module.exports = {
  mode: "production",
  // The bundle is loaded from disk by the game (no network): webpack's 244 KiB web download hint does not apply.
  performance: { hints: false },
  stats: "none",
  entry: {
    [MOD.id]: "./src/index.tsx",
  },
  externalsType: "window",
  externals: {
    react: "React",
    "react-dom": "ReactDOM",
    "cs2/modding": "cs2/modding",
    "cs2/api": "cs2/api",
    "cs2/bindings": "cs2/bindings",
    "cs2/l10n": "cs2/l10n",
    "cs2/ui": "cs2/ui",
    "cs2/input": "cs2/input",
    "cs2/utils": "cs2/utils",
    "cohtml/cohtml": "cohtml/cohtml",
  },
  module: {
    rules: [
      {
        test: /\.tsx?$/,
        use: "ts-loader",
        exclude: /node_modules/,
      },
      {
        test: /\.s?css$/,
        include: path.join(__dirname, "src"),
        use: [
          MiniCssExtractPlugin.loader,
          {
            loader: "css-loader",
            options: {
              url: true,
              importLoaders: 1,
              modules: {
                auto: true,
                exportLocalsConvention: "camelCase",
                localIdentName: "[local]_[hash:base64:3]",
              },
            },
          },
          {
            loader: "sass-loader",
            // Expanded output, no colour rewriting (Gameface rejects hsla, which minifiers may emit).
            options: { api: "modern", sassOptions: { style: "expanded" } },
          },
        ],
      },
      // Every bundled file is namespaced under images/citymaker/ with a content hash: coui://ui-mods/
      // is shared by ALL mods, so a plain images/check.svg could be overwritten by another mod's file.
      {
        test: /\.svg$/i,
        resourceQuery: /tint=/,
        type: "asset/resource",
        use: [path.resolve(__dirname, "tools/svg-tint-loader.js")],
        generator: {
          filename: (pathData) => {
            const m = /tint=(\w+)/.exec((pathData.module && pathData.module.resource) || "");
            return `images/citymaker/[name]-${m ? m[1] : "light"}.[contenthash:6][ext]`;
          },
        },
      },
      {
        test: /\.(png|jpe?g|gif|svg)$/i,
        resourceQuery: { not: [/tint=/] },
        type: "asset/resource",
        generator: {
          filename: "images/citymaker/[name].[contenthash:6][ext]",
        },
      },
    ],
  },
  resolve: {
    extensions: [".tsx", ".ts", ".js"],
    modules: ["node_modules", path.join(__dirname, "src")],
    alias: {
      "mod.json": path.resolve(__dirname, "mod.json"),
    },
  },
  output: {
    path: path.resolve(__dirname, OUTPUT_DIR),
    library: {
      type: "module",
    },
    publicPath: `coui://ui-mods/`,
    // Drop stale images (e.g. the old un-namespaced images/*.svg); never touch anything else in the
    // output folder (it can be the game Mods folder holding the DLL).
    clean: { keep: (asset) => !/^images[\/]/.test(asset) },
  },
  optimization: {
    minimize: true,
    minimizer: [
      new TerserPlugin({
        extractComments: {
          banner: () => banner,
        },
      }),
    ],
  },
  experiments: {
    outputModule: true,
  },
  plugins: [
    new MiniCssExtractPlugin(),
    new CSSPresencePlugin(),
    // Fails the build on CSS Gameface rejects (grid, hsla, var() in shorthands, :not, clamp…).
    new CssLintPlugin(),
    {
      apply(compiler) {
        let runCount = 0;
        compiler.hooks.done.tap("AfterDonePlugin", (stats) => {
          console.log(stats.toString({ colors: true }));
          console.log(`\n🔨 ${!runCount++ ? "Built" : "Updated"} ${MOD.id}`);
          console.log("   " + gray(OUTPUT_DIR) + "\n");
        });
      },
    },
  ],
};
