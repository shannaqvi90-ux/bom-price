import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  globalIgnores(['dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      ecmaVersion: 2020,
      globals: globals.browser,
    },
    rules: {
      // Phase 3 dark mode hygiene: prevent regression to hardcoded Tailwind
      // gray/slate/white classes. Phase 2 (PR #80) migrated all 223 of these
      // to semantic theme tokens defined in src/index.css's @theme inline
      // block. New code must use text-foreground / text-muted-foreground /
      // bg-card / bg-muted / border-border / divide-border directly.
      //
      // Scope: only inside JSXAttribute className strings + template literals.
      // Status colors (bg-blue-100, text-amber-700 etc.) are NOT banned —
      // they require dark: variants per the same spec but are too contextual
      // for a simple lint rule. See docs/superpowers/specs/2026-05-03-phase-2-dark-mode-design.md.
      'no-restricted-syntax': [
        'error',
        {
          selector:
            "JSXAttribute[name.name='className'] Literal[value=/\\b(text|bg|border|divide)-(gray|slate)-[0-9]+\\b/]",
          message:
            'Hardcoded gray/slate classes are banned. Use semantic tokens: text-foreground, text-muted-foreground, bg-card, bg-muted, border-border, divide-border. See Phase 2 dark mode spec.',
        },
        {
          selector:
            "JSXAttribute[name.name='className'] TemplateElement[value.raw=/\\b(text|bg|border|divide)-(gray|slate)-[0-9]+\\b/]",
          message:
            'Hardcoded gray/slate classes are banned. Use semantic tokens: text-foreground, text-muted-foreground, bg-card, bg-muted, border-border, divide-border. See Phase 2 dark mode spec.',
        },
        {
          selector: "JSXAttribute[name.name='className'] Literal[value=/\\bbg-white\\b/]",
          message: 'bg-white is banned. Use bg-card. See Phase 2 dark mode spec.',
        },
        {
          selector:
            "JSXAttribute[name.name='className'] TemplateElement[value.raw=/\\bbg-white\\b/]",
          message: 'bg-white is banned. Use bg-card. See Phase 2 dark mode spec.',
        },
      ],
    },
  },
])
