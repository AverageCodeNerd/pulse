# Code signing & SmartScreen

## Why the "unknown publisher" warning happens

When someone runs `Pulse-Setup.exe`, Windows **SmartScreen** may show
*"Windows protected your PC — unknown publisher."* This is **not** because Pulse
is unsafe — it's because the file isn't **code-signed** with a certificate tied
to a verified identity, and it hasn't built up download "reputation" yet.

There is **no free way** to remove this warning instantly. It requires a
code-signing certificate, which involves an identity check and a cost. This is a
purchase only the project owner can make. Everything else is already wired up.

## The cheapest good option: Azure Trusted Signing (~$9.99/month)

Microsoft's own signing service. It's the cheapest route and, because Microsoft
runs it, signatures get **good SmartScreen reputation quickly**.

1. In the [Azure portal](https://portal.azure.com), create a **Trusted Signing
   account** (search "Trusted Signing"). Pick the Basic plan (~$9.99/mo).
2. Complete **identity verification** (individual or organization). Individual
   verification is quick with a government ID.
3. Create a **Certificate Profile** under the account.
4. Install the client tooling: the **Azure.CodeSigning.Dlib** (via
   `winget install Microsoft.Azure.TrustedSigningClientTools` or the NuGet
   `Microsoft.Trusted.Signing.Client`), and create a metadata JSON with your
   account/endpoint/profile (Microsoft's docs have the template).
5. Point Pulse's build at it and run the release:

   ```powershell
   $env:PULSE_TS_DLIB     = "C:\path\to\Azure.CodeSigning.Dlib.dll"
   $env:PULSE_TS_METADATA = "C:\path\to\trusted-signing-metadata.json"
   ./installer/build-release.ps1
   ```

That's it — `Pulse.exe` and `Pulse-Setup.exe` come out signed.

## Alternative: a bought PFX certificate

If you buy an OV/EV code-signing cert from a CA (DigiCert, Sectigo, SSL.com, …):

```powershell
$env:PULSE_CERT_PFX  = "C:\path\to\cert.pfx"
$env:PULSE_CERT_PASS = "your-pfx-password"
./installer/build-release.ps1
```

Notes:
- **OV** certs (~$100–250/yr) still need to earn SmartScreen reputation over
  time/downloads. **EV** certs (~$250–400/yr, hardware token) get instant
  reputation but are pricier and clunkier. For an indie app, **Azure Trusted
  Signing is usually the best value.**
- Signing uses SHA-256 with an RFC-3161 timestamp, so signatures remain valid
  after the certificate expires.

## Until it's signed

The download page and release notes tell users to click
**More info → Run anyway** on the SmartScreen prompt. The portable zip avoids the
installer prompt (though the app exe is still unsigned).
