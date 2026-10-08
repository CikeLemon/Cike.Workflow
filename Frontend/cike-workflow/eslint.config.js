/**
 * .eslint.js
 *
 * ESLint configuration file.
 */

import pluginVue from 'eslint-plugin-vue'
import vueTsEslintConfig from '@vue/eslint-config-typescript'

export default [
  {
    name: 'app/files-to-lint',
    files: ['**/*.{ts,mts,tsx,vue}'],
  },

  {
    name: 'app/files-to-ignore',
    ignores: ['**/dist/**', '**/dist-ssr/**', '**/coverage/**'],
  },

  ...pluginVue.configs['flat/recommended'],
  ...vueTsEslintConfig(),

  {
    rules: {
      '@typescript-eslint/no-unused-expressions': [
        'error',
        {
          allowShortCircuit: true,
          allowTernary: true,
        },
      ],
      'vue/multi-word-component-names': 'off',
      // Enforce SFC top-level block order: template -> script -> style.
      'vue/block-order': ['error', { order: ['template', 'script', 'style'] }],
      'vue/max-attributes-per-line': 'off',
      'vue/attributes-order': 'off',
      'vue/singleline-html-element-content-newline': 'off',
      'vue/first-attribute-linebreak': 'off',
      '@typescript-eslint/no-explicit-any': 'off',
      "vue/html-self-closing": 'off',
      "@typescript-eslint/no-unused-vars": 'off',
    }
  },

  {
    // shadcn-vue CLI generates src/components/ui/** as script-first; do not
    // hand-modify or re-order these generated components.
    name: 'app/ui-generated-exempt',
    files: ['src/components/ui/**/*.vue'],
    rules: {
      'vue/block-order': 'off',
    },
  }
]
