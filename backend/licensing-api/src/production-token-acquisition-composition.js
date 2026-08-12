import { createProductionAuthenticatedAccountResolver } from "./production-authenticated-account-adapter.js";
import { createProductionTokenAcquisitionHandler } from "./production-token-acquisition.js";

export function createProductionTokenAcquisitionComposition({
  verifySession,
  identitySecret,
  productId,
  clock,
  lifetimeSeconds
} = {}) {
  const resolveAuthenticatedAccount = createProductionAuthenticatedAccountResolver({
    verifySession,
    ...(productId === undefined ? {} : { productId })
  });

  return createProductionTokenAcquisitionHandler({
    resolveAuthenticatedAccount,
    identitySecret,
    ...(productId === undefined ? {} : { productId }),
    ...(clock === undefined ? {} : { clock }),
    ...(lifetimeSeconds === undefined ? {} : { lifetimeSeconds })
  });
}
