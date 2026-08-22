import {
  createProductionAuthenticatedAccountResolver
} from "./production-authenticated-account-adapter.js";

import {
  createProductionTokenIssuer
} from "./production-token-issuer.js";

import {
  D1InstallationAuthorizationStore
} from "./installation-authorization-store.js";

import {
  createInstallationAuthorizationService
} from "./installation-authorization-service.js";

import {
  createInstallationAuthorizationHttpAdapter
} from "./installation-authorization-http-adapter.js";

const DEFAULT_PRODUCT_ID = "pcspa-pro";
const DEFAULT_AUTHORIZATION_LIFETIME_SECONDS = 300;
const DEFAULT_TOKEN_LIFETIME_SECONDS = 300;

export function createProductionInstallationAuthorizationComposition({
  database,
  verifySession,
  identitySecret,
  productId = DEFAULT_PRODUCT_ID,
  authorizationClock,
  tokenClock,
  randomBytes,
  authorizationLifetimeSeconds =
    DEFAULT_AUTHORIZATION_LIFETIME_SECONDS,
  tokenLifetimeSeconds =
    DEFAULT_TOKEN_LIFETIME_SECONDS
} = {}) {
  const store =
    new D1InstallationAuthorizationStore(
      database
    );

  const resolveAuthenticatedAccount =
    createProductionAuthenticatedAccountResolver({
      verifySession,
      productId
    });

  const authorizationService =
    createInstallationAuthorizationService({
      store,

      ...(authorizationClock === undefined
        ? {}
        : {
            clock: authorizationClock
          }),

      ...(randomBytes === undefined
        ? {}
        : {
            randomBytes
          }),

      lifetimeSeconds:
        authorizationLifetimeSeconds
    });

  const issueProductionToken =
    createProductionTokenIssuer({
      secret: identitySecret,

      ...(tokenClock === undefined
        ? {}
        : {
            clock: tokenClock
          }),

      lifetimeSeconds:
        tokenLifetimeSeconds
    });

  return createInstallationAuthorizationHttpAdapter({
    resolveAuthenticatedAccount,
    authorizationService,
    issueProductionToken,
    tokenLifetimeSeconds
  });
}