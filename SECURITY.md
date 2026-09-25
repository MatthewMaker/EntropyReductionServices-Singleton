# Security policy

## Reporting a vulnerability

Please report vulnerabilities privately through GitHub: on this repository's **Security** tab,
choose **Report a vulnerability**. If you cannot use that, email
security@entropyreductionservices.com. Do not open a public issue or pull request for a security
problem.

That includes problems in the Roslyn analyzers, which run inside the compiler of every assembly
that references this package, and in the release pipeline that signs and publishes it.

## Supported versions

Security fixes are released only as a new version on top of the latest release. Older versions
are not patched; upgrade to receive a fix.
