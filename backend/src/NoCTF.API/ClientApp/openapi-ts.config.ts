import { defineConfig } from '@hey-api/openapi-ts'

export default defineConfig({
  input: '../wwwroot/openapi/v1.json',
  output: {
    path: 'app/api',
    clean: true,
  },
  plugins: [
    {
      name: '@hey-api/client-fetch',
      baseUrl: false,
    },
    {
      name: '@hey-api/sdk',
    },
  ],
})
