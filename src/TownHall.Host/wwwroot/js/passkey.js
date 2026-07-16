// WebAuthn passkey helpers. `register`/`authenticate` take the server-built options JSON (from
// Fido2's CredentialCreateOptions/AssertionOptions), drive navigator.credentials, and return the
// browser's response as JSON in the shape Fido2NetLib expects (base64url for every byte buffer).

function b64urlToBuf(b64url) {
    const pad = '='.repeat((4 - (b64url.length % 4)) % 4);
    const b64 = (b64url + pad).replace(/-/g, '+').replace(/_/g, '/');
    const bin = atob(b64);
    const buf = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++)
        buf[i] = bin.charCodeAt(i);

    return buf.buffer;
}

function bufToB64url(buf) {
    const bytes = new Uint8Array(buf);
    let bin = '';
    for (let i = 0; i < bytes.length; i++)
        bin += String.fromCharCode(bytes[i]);

    return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export async function register(optionsJson) {
    const options = JSON.parse(optionsJson);
    options.challenge = b64urlToBuf(options.challenge);
    options.user.id = b64urlToBuf(options.user.id);
    if (options.excludeCredentials)
        options.excludeCredentials.forEach(c => c.id = b64urlToBuf(c.id));

    const cred = await navigator.credentials.create({ publicKey: options });
    return JSON.stringify({
        id: cred.id,
        rawId: bufToB64url(cred.rawId),
        type: cred.type,
        extensions: cred.getClientExtensionResults(),
        response: {
            attestationObject: bufToB64url(cred.response.attestationObject),
            clientDataJSON: bufToB64url(cred.response.clientDataJSON),
            transports: cred.response.getTransports ? cred.response.getTransports() : []
        }
    });
}

export async function authenticate(optionsJson) {
    const options = JSON.parse(optionsJson);
    options.challenge = b64urlToBuf(options.challenge);
    if (options.allowCredentials)
        options.allowCredentials.forEach(c => c.id = b64urlToBuf(c.id));

    const assertion = await navigator.credentials.get({ publicKey: options });
    return JSON.stringify({
        id: assertion.id,
        rawId: bufToB64url(assertion.rawId),
        type: assertion.type,
        extensions: assertion.getClientExtensionResults(),
        response: {
            authenticatorData: bufToB64url(assertion.response.authenticatorData),
            clientDataJSON: bufToB64url(assertion.response.clientDataJSON),
            signature: bufToB64url(assertion.response.signature),
            userHandle: assertion.response.userHandle ? bufToB64url(assertion.response.userHandle) : null
        }
    });
}
