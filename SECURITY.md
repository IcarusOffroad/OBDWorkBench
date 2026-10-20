# Security and sensitive reports

For issues involving sensitive data or a vulnerability, do not post private logs, coordinates, credentials or exploit details in a public issue. Contact the repository maintainer privately through an established contact channel; this source package does not designate a public security email or response-time commitment.

Workbench creates local logs and optional support bundles; it does not upload them automatically. Support ZIP sanitization is described in [SUPPORT_BUNDLES.md](docs/SUPPORT_BUNDLES.md). GPS coordinates and route history are not automatically redacted.

Icarus GPS data is shared with interactive users on the local Windows PC. Configuration/control uses a separate administrator-protected pipe. The subscriber is intended for a trusted personal PC; it is not a multi-user confidentiality boundary. See the bundled [interface documentation](GPSService/IcarusGPS_v0.2.4/Integration/README.md).
