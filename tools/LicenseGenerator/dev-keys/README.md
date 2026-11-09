# Development / test keys — NOT FOR PRODUCTION

These keys exist only so the licensing flow can be tested end-to-end during development.

* **Debug** builds of the application trust `dev-test-public.txt`.
* **Release** builds do **not** trust it. Release builds trust only the production public key in
  `src/BurhanScreenCapture/Licensing/ProductionPublicKey.txt`.

Because the private key is public in this repository, any license signed with it is worthless in a
Release build. Generate your own production key pair with `LicenseGenerator keygen` and keep the
production private key offline (see `docs/LICENSING.md`).
