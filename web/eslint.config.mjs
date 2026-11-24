import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";

import { sizeBaseline } from "./eslint.size-baseline.mjs";

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  // Size limits from docs/conventions.md §7. Comments, URLs and long strings don't count against line length.
  {
    files: ["**/*.{ts,tsx}"],
    rules: {
      "max-len": ["error", { code: 140, ignoreUrls: true, ignoreStrings: true, ignoreTemplateLiterals: true, ignoreComments: true, ignoreRegExpLiterals: true }],
      "max-lines": ["error", { max: 300, skipBlankLines: true, skipComments: true }],
      "max-lines-per-function": ["error", { max: 60, skipBlankLines: true, skipComments: true, IIFEs: true }],
    },
  },
  // Tests describe many cases in one block.
  { files: ["**/*.test.{ts,tsx}", "e2e/**"], rules: { "max-lines-per-function": "off", "max-lines": "off" } },
  // Files that were already too long: warnings until they are split (see eslint.size-baseline.mjs).
  { files: sizeBaseline, rules: { "max-len": "warn", "max-lines": "warn", "max-lines-per-function": "warn" } },
  // Override default ignores of eslint-config-next.
  globalIgnores([
    // Default ignores of eslint-config-next:
    ".next/**",
    "out/**",
    "build/**",
    "next-env.d.ts",
    "src/lib/api/schema.d.ts", // generated
  ]),
]);

export default eslintConfig;
