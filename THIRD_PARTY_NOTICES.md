# Third-Party Notices / 第三方声明

This repository's own source code is licensed under the MIT License in
[`LICENSE`](LICENSE). That project license does not relicense bundled
third-party binaries or frontend assets. Keep this file, the files under
`BundledTools/ipmitool/LICENSES/`, and the ECharts license/notice files with
source and binary distributions.

本仓库自身源码使用 [`LICENSE`](LICENSE) 中的 MIT License。该项目许可证不会重新授权随包分发的第三方二进制或前端资产。源码和二进制发布时都应保留本文件、`BundledTools/ipmitool/LICENSES/` 下的文件，以及 ECharts 的许可证和 NOTICE 文件。

## Bundled ipmitool Package / 内置 ipmitool 包

The Windows command bundle lives under `BundledTools/ipmitool/` and is copied
to application output. It contains:

| File | Observed version | SHA-256 |
| --- | --- | --- |
| `ipmitool.exe` | `1.8.15` from `ipmitool -V` | `EB869FC08CFBA8906BED32168D002FCDC4BA2CD0324FB81005DD9C20F3C6A8BD` |
| `cygwin1.dll` | `2.5.2` file metadata | `A2097A9C2669E88E5EE023A11F3C45D8121013F09EFF41CF1C07B228FDA34079` |
| `cygcrypto-1.0.0.dll` | OpenSSL `1.0.2n` file metadata | `83D9DF20DFF0EF47C731B2313E31AB51665F38C21F449E9EAE6D8DFFD7E7535C` |
| `cyggcc_s-1.dll` | GCC runtime; file metadata does not declare a version | `F62E3AB688DA05EF57D4CD592C3956E91F381E9A712006BD24698A58BDF52250` |
| `cygz.dll` | zlib `1.2.8` file metadata | `9753C5C8E5DD8816C0B046165DF3BB28F201A403F901D7B611AE98C18743D0C2` |

License and source references:

- `ipmitool.exe`: BSD-style IPMItool license. See `BundledTools/ipmitool/LICENSES/ipmitool-BSD.txt`; upstream project: <https://github.com/ipmitool/ipmitool> and <https://sourceforge.net/projects/ipmitool/>.
- `cygwin1.dll`: Cygwin API library, LGPLv3 or later according to the Cygwin licensing page, with Cygwin's documented linking exception. See `BundledTools/ipmitool/LICENSES/cygwin-LICENSE-NOTICE.txt`; upstream licensing: <https://cygwin.com/licensing.html>.
- `cygcrypto-1.0.0.dll`: OpenSSL 1.0.2n, dual OpenSSL/SSLeay license. See `BundledTools/ipmitool/LICENSES/openssl-102n-LICENSE.txt`; upstream license note: <https://openssl-library.org/source/license/>.
- `cyggcc_s-1.dll`: GCC runtime library, GPLv3 with the GCC Runtime Library Exception for the runtime components. See `BundledTools/ipmitool/LICENSES/gcc-runtime-exception-NOTICE.txt`; package/source references: <https://cygwin.com/packages/summary/libgcc1.html> and <https://cygwin.com/packages/summary/gcc-src.html>.
- `cygz.dll`: zlib 1.2.8 license. See `BundledTools/ipmitool/LICENSES/zlib-128-LICENSE.txt`; upstream license: <https://zlib.net/zlib_license.html>.

If any bundled binary is replaced, update this file, `BundledTools/ipmitool/README.md`, the matching license files, version notes, hashes, and release-script required-file checks in the same change. Do not replace these files silently.

如替换任何内置二进制，必须同步更新本文件、`BundledTools/ipmitool/README.md`、对应许可证文件、版本说明、哈希值和发布脚本的必需文件检查。不要静默替换这些文件。

## ECharts

The local dashboard uses Apache ECharts from `Assets/Charts/echarts.min.js`.
The corresponding Apache License and NOTICE are kept as:

- `Assets/Charts/echarts.LICENSE.txt`
- `Assets/Charts/echarts.NOTICE.txt`

Release packages must include both files together with `echarts.min.js`.

本地图表使用 `Assets/Charts/echarts.min.js` 中的 Apache ECharts。发布包必须同时保留 `Assets/Charts/echarts.LICENSE.txt` 和 `Assets/Charts/echarts.NOTICE.txt`。
