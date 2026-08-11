import { D1LicensingEventStore } from "./licensing-event-store.js";
import { D1DeviceActivationStore } from "./device-activation-store.js";
import { LicensingCompatibilityService } from "./licensing-compatibility-service.js";
import { createLicensingHttpAdapter } from "./licensing-http-adapter.js";

export function createLicensingComposition({ database, idFactory, clock } = {}) {
  const eventStore = new D1LicensingEventStore(database);
  const deviceStore = new D1DeviceActivationStore(database);

  const service = new LicensingCompatibilityService({
    eventStore,
    deviceStore,
    ...(idFactory === undefined ? {} : { idFactory })
  });

  const adapter = createLicensingHttpAdapter({
    service,
    ...(clock === undefined ? {} : { clock })
  });

  return Object.freeze({ eventStore, deviceStore, service, adapter });
}
