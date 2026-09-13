// Flat config (ESLint 9/10+). This is what makes eslint actually look at
// the inline <script> blocks in assets/index.html -- eslint-plugin-html
// has to be registered here; passing "--plugin html" on the CLI alone
// does nothing under flat config (that only worked pre-v9 .eslintrc.json).
import html from "eslint-plugin-html";

export default [
  {
    files: ["**/*.html"],
    plugins: { html },
  },
];
