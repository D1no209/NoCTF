export function passkeyBrowserSupported(): boolean {
  return typeof window !== 'undefined' && window.isSecureContext && typeof PublicKeyCredential !== 'undefined'
    && typeof PublicKeyCredential.parseCreationOptionsFromJSON === 'function'
    && typeof PublicKeyCredential.parseRequestOptionsFromJSON === 'function'
    && typeof navigator.credentials?.create === 'function' && typeof navigator.credentials?.get === 'function'
}
function encode(value: ArrayBuffer | null | undefined): string | undefined {
  if (!value) return undefined
  return btoa(String.fromCharCode(...new Uint8Array(value))).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '')
}
// One explicit protocol mapping avoids depending on password managers' overridden toJSON methods.
export function passkeyCredentialJson(credential: PublicKeyCredential): string {
  const response = credential.response
  const registration = response instanceof AuthenticatorAttestationResponse
  return JSON.stringify({ id: credential.id, rawId: encode(credential.rawId), type: credential.type,
    authenticatorAttachment: credential.authenticatorAttachment, clientExtensionResults: credential.getClientExtensionResults(),
    response: registration ? { clientDataJSON: encode(response.clientDataJSON), attestationObject: encode(response.attestationObject), transports: response.getTransports() }
      : { clientDataJSON: encode(response.clientDataJSON), authenticatorData: encode((response as AuthenticatorAssertionResponse).authenticatorData),
          signature: encode((response as AuthenticatorAssertionResponse).signature), userHandle: encode((response as AuthenticatorAssertionResponse).userHandle) } })
}
export async function createPasskey(publicKey: unknown, signal: AbortSignal): Promise<string> {
  const options = PublicKeyCredential.parseCreationOptionsFromJSON(publicKey as PublicKeyCredentialCreationOptionsJSON)
  const credential = await navigator.credentials.create({ publicKey: options, signal })
  if (!(credential instanceof PublicKeyCredential)) throw new DOMException('Authenticator unavailable', 'NotSupportedError')
  return passkeyCredentialJson(credential)
}
export async function requestPasskey(publicKey: unknown, signal: AbortSignal): Promise<string> {
  const options = PublicKeyCredential.parseRequestOptionsFromJSON(publicKey as PublicKeyCredentialRequestOptionsJSON)
  const credential = await navigator.credentials.get({ publicKey: options, signal })
  if (!(credential instanceof PublicKeyCredential)) throw new DOMException('Authenticator unavailable', 'NotSupportedError')
  return passkeyCredentialJson(credential)
}
export function passkeyCancelled(error: unknown): boolean {
  return error instanceof DOMException && (error.name === 'AbortError' || error.name === 'NotAllowedError')
}
