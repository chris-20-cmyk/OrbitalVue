import { cloudflareTest } from "@cloudflare/vitest-plugin";
import { defineConfig } from "vitest/config";

const testSecrets = {
  GOOGLE_SERVICE_ACCOUNT_EMAIL: "service-account@example.invalid",
  GOOGLE_SERVICE_ACCOUNT_PRIVATE_KEY: "LOCAL_TEST_KEY_ONLY",
  SAMSUNG_DPI_SECURITY_KEY: "LOCAL_TEST_KEY_ONLY",
  RATE_LIMIT_KEY_SECRET: "LOCAL_TEST_RATE_LIMIT_SECRET_AT_LEAST_32_CHARS"
};

Object.assign(process.env, testSecrets);

export default defineConfig({
  plugins: [
    cloudflareTest({
      main: "./src/index.ts",
      wrangler: { configPath: "./wrangler.jsonc" },
      miniflare: {
        bindings: {
          DEPLOYMENT_ENVIRONMENT: "local",
          EXPECTED_HOSTNAME: "entitlements.orbitalvue.test",
          ALLOWED_BROWSER_ORIGINS: "[]",
          GOOGLE_PLAY_PACKAGE_NAME: "com.orbitalvue.player",
          GOOGLE_PLAY_PRODUCT_ID: "orbitalvue_premium_once",
          GOOGLE_PLAY_ALLOW_TEST_PURCHASES: "false",
          SAMSUNG_CHECKOUT_APP_ID: "OrbitalVueCheckout",
          SAMSUNG_PREMIUM_PRODUCT_ID: "orbitalvue_premium",
          ...testSecrets
        }
      }
    })
  ],
  test: {
    include: ["test/**/*.test.ts"]
  }
});
