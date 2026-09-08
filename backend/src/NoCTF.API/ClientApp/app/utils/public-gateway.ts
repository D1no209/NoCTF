import type { NoCtfapiEndpointsRuntimePublicAccessFailureProtocol, NoCtfapiEndpointsRuntimePublicAccessStateProtocol } from '../api'
import { translate } from './i18n'

const failures = {
  GatewayDisabled: "ui.challengeTunnelingIsDisabled",
  ConnectorOffline: "ui.thePublicConnectorIsTemporarilyOffline",
  PublicPortUnavailable: "ui.thePortIsNotAllowedOrIsAlreadyOccupied",
  RuntimeBindingUnavailable: "ui.theInstancePortBindingCouldNotBeVerified",
  UnsupportedRuntimeKind: "ui.thisRuntimeTypeDoesNotSupportTunnelingYet",
  AccessDisplayUnsupported: "ui.connectionTextMustIncludeAnd",
  GatewayCapacityExceeded: "ui.thePublicPortQuotaIsExhausted",
  GatewayIdentityRejected: "ui.publicInstanceIdentityVerificationFailed",
  GatewaySafetyCheckFailed: "ui.publicAccessSafetyChecksDidNotPass",
  GatewayReconciliationPending: "ui.thePublicConnectionIsBeingPreparedOrUpdated",
} satisfies Record<NoCtfapiEndpointsRuntimePublicAccessFailureProtocol, string>
const states = {
  Disabled: "ui.challengeTunnelingIsDisabled", Pending: "ui.thePublicConnectionIsBeingPreparedOrUpdated", Ready: "ui.thePublicEntryPointIsReady",
  Unavailable: "ui.thePublicEntryPointIsTemporarilyUnavailable", Revoking: "ui.revokingThePublicEntryPoint", Unsupported: "ui.thisRuntimeTypeDoesNotSupportTunnelingYet",
} satisfies Record<NoCtfapiEndpointsRuntimePublicAccessStateProtocol, string>

export function publicGatewayFailure(value?: NoCtfapiEndpointsRuntimePublicAccessFailureProtocol | null): string {
  return value ? translate(failures[value]) : ''
}
export function publicGatewayState(value?: NoCtfapiEndpointsRuntimePublicAccessStateProtocol): string {
  return value ? translate(states[value]) : translate("ui.publicAccessStatusIsAwaitingConfirmation")
}
export function gatewayOrigin(value: string, https = false): string | null {
  try {
    const url = new URL(value)
    return (https ? url.protocol === 'https:' : ['http:', 'https:'].includes(url.protocol))
      && !url.username && !url.password && url.pathname === '/' && !url.search && !url.hash ? url.origin.toLowerCase() : null
  }
  catch { return null }
}

/** Same ASCII DNS / canonical IPv4 contract as PublicGatewayPolicyRules.Host. */
export function gatewayHost(value: string): boolean {
  if (!value.length || value.length > 253) return false
  if (/^[0-9.]+$/.test(value)) {
    const octets = value.split('.')
    return octets.length === 4 && octets.every(octet => /^(0|[1-9][0-9]{0,2})$/.test(octet) && Number(octet) <= 255)
  }
  const host = value.endsWith('.') ? value.slice(0, -1) : value
  return host.split('.').every(label => label.length > 0 && label.length <= 63
    && /^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/i.test(label))
}
