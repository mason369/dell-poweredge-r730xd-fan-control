# Bundled ipmitool Runtime

This directory contains the Windows `ipmitool` runtime used by the app. It is
copied into build, publish, Release zip, and MSIX outputs.

The app project is MIT-licensed, but the files in this directory keep their
own upstream licenses. Do not remove this README or the `LICENSES/` directory
from source or binary distributions.

## Files

| File | Observed version | SHA-256 |
| --- | --- | --- |
| `ipmitool.exe` | `1.8.15` from `ipmitool -V` | `EB869FC08CFBA8906BED32168D002FCDC4BA2CD0324FB81005DD9C20F3C6A8BD` |
| `cygwin1.dll` | `2.5.2` file metadata | `A2097A9C2669E88E5EE023A11F3C45D8121013F09EFF41CF1C07B228FDA34079` |
| `cygcrypto-1.0.0.dll` | OpenSSL `1.0.2n` file metadata | `83D9DF20DFF0EF47C731B2313E31AB51665F38C21F449E9EAE6D8DFFD7E7535C` |
| `cyggcc_s-1.dll` | GCC runtime; file metadata does not declare a version | `F62E3AB688DA05EF57D4CD592C3956E91F381E9A712006BD24698A58BDF52250` |
| `cygz.dll` | zlib `1.2.8` file metadata | `9753C5C8E5DD8816C0B046165DF3BB28F201A403F901D7B611AE98C18743D0C2` |

## License Files

- `LICENSES/ipmitool-BSD.txt`
- `LICENSES/cygwin-LICENSE-NOTICE.txt`
- `LICENSES/openssl-102n-LICENSE.txt`
- `LICENSES/gcc-runtime-exception-NOTICE.txt`
- `LICENSES/zlib-128-LICENSE.txt`

## Source References

- IPMItool upstream: <https://github.com/ipmitool/ipmitool> and <https://sourceforge.net/projects/ipmitool/>.
- Cygwin licensing: <https://cygwin.com/licensing.html>.
- Cygwin source package references: <https://cygwin.com/packages/summary/cygwin-src.html> and <https://cygwin.com/packages/summary/gcc-src.html>.
- OpenSSL 1.0.2 historical source/license note: <https://openssl-library.org/source/license/>.
- zlib license/source: <https://zlib.net/zlib_license.html> and <https://zlib.net/fossils/>.

## Maintenance Rule

When replacing any file in this directory, update versions, hashes,
`THIRD_PARTY_NOTICES.md`, and the required-file lists in:

- `tools/Publish-UnpackagedExe.ps1`
- `tools/Publish-ReleaseZip.ps1`
- `tools/Publish-SignedMsix.ps1`

Missing license or notice files should stop packaging instead of producing a
release that looks complete but is missing redistribution information.
