import { LicensingIdentityError } from "./licensing-identity-bridge.js";
import { createLicensingWorkerRuntime } from "./licensing-worker-runtime.js";
import {
  ProductionIdentityError,
  createProductionIdentityResolver
} from "./production-identity-verifier.js";

export function createProductionLicensingRuntime({
  env,
  identitySecret,
  productId,
  idFactory,
  clock
} = {}) {
  const productionResolver = createProductionIdentityResolver({
    secret: identitySecret,
    ...(clock === undefined ? {} : { clock })
  });

  const resolveAuthenticatedAccount = async request => {
    try {
      return await productionResolver(request);
    } catch (error) {
      if (error instanceof ProductionIdentityError) {
        throw new LicensingIdentityError("unauthenticated");
      }
      throw error;
    }
  };

  return createLicensingWorkerRuntime({
    env,
    resolveAuthenticatedAccount,
    ...(productId === undefined ? {} : { productId }),
    ...(idFactory === undefined ? {} : { idFactory }),
    ...(clock === undefined ? {} : { clock })
  });
}
