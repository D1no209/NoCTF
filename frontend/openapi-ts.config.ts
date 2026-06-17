import { defineConfig } from '@hey-api/openapi-ts';

export default defineConfig({
  input: '../backend/artifacts/openapi/swagger.json',
  output: 'src/api/generated',
  client: '@hey-api/client-fetch',
});
