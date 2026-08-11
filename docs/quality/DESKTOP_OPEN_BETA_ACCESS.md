# Desktop Open Beta access boundary

## Current build policy

PC-SPA Beta builds are activation-free. The desktop does not request an
account password, activation key, entitlement, device registration, or
licensing-service connection. Each build evaluates its embedded official UTC
release timestamp and remains usable for exactly 30 days from that timestamp.

The release and expiry timestamps shown in Settings > Beta Access come from
`BetaBuildPolicy`; they are not supplied by a remote service or local user
setting.

## Removed legacy runtime

The previous desktop authentication, activation-key, device-identity,
licence-token storage, licence validation, and controlled-Beta access-code
services were removed. Their hidden XAML controls and startup construction
paths were removed with them.

## Future commercial boundary

Commercial licensing must be introduced as a new, separately reviewed
implementation. It must use the approved browser-based account activation and
signed offline-entitlement design. It must not restore the deleted password or
activation-key flow, and it must not change the activation-free behavior of an
already published Open Beta build.
