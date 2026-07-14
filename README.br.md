[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# Controle de ventiladores Dell PowerEdge R730xd via iDRAC

Aplicativo WinUI 3 para Windows 10/11 que controla ventiladores e monitora o hardware do Dell PowerEdge R730xd via iDRAC/IPMI. Reúne velocidades manuais, Dell Auto, curvas por temperatura da CPU ou potência, sensores BMC SDR, Fan 1-6 RPM, gráficos locais, predefinições e ações na bandeja.

## Download e escopo

- Baixe `DellR730xdFanControlCenter-win-x64.zip` na [versão mais recente do GitHub](https://github.com/mason369/dell-poweredge-r730xd-fan-control/releases/latest), extraia tudo e execute `DellR730xdFanControlCenter.exe`.
- O código-fonte atual e o Release publicado mais recente são `v1.1.2`; os arquivos exe/dll do pacote têm versão `1.1.2.0`, portanto o código-fonte, a tag e os binários identificam a mesma versão.
- O hardware de destino é Dell PowerEdge R730xd. Somente R730xd com iDRAC 2.82 foi observado localmente; outros firmwares exigem validação supervisionada.

## Recursos

- Controle de todos os ventiladores de `0-100%`, predefinições manuais e retorno do controle ao Dell Auto.
- Ajuste automático por temperatura da CPU e curvas editáveis de temperatura-ventilador ou potência-ventilador.
- Execução real de `mc info` e `sdr elist` para temperatura, RPM, potência, tensão, corrente, redundância e estados discretos.
- Sete dias de histórico JSONL local com ECharts/WebView2, 22 idiomas de UI, menu de bandeja e armazenamento opcional de senha protegido por DPAPI.

## Início rápido

1. Ative **IPMI over LAN** no iDRAC e verifique a rede de gerenciamento.
2. Execute o Release extraído ou o código-fonte: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. Na página de configurações, informe endereço iDRAC/BMC, usuário e senha. Ative DPAPI somente se precisar de conexão automática depois.
4. Ao salvar, o aplicativo executa primeiro `mc info` e depois `sdr elist`. A consulta periódica e o estado de sucesso só começam após o sucesso real dos comandos e da gravação do log.
5. Comece com Dell Auto ou valores prudentes de `20%`/`35%` e observe temperaturas e RPM.

## Padrões e arquivos locais

- Consulta `1 s`, tempo limite `35 s`, temperaturas alvo/alta/emergência `68 °C` / `78 °C` / `84 °C`, faixa automática `10-42%`, histórico `7` dias.
- Configurações: `%LocalAppData%\DellR730xdFanControlCenter\settings.json`.
- Logs: `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`.
- Histórico e WebView2: `%LocalAppData%\DellR730xdFanControlCenter\chart-history`, `%LocalAppData%\DellR730xdFanControlCenter\WebView2`.
- A senha é enviada ao `ipmitool -E` por `IPMI_PASSWORD` e não aparece nos argumentos da linha de comando.

## Falhas e limites de hardware

- Falhas de autenticação, rede, SDR, WebView2, comando raw ou gravação do log são exibidas e registradas; não aparecem como sucesso.
- Se o modo manual for ativado, mas o comando percentual seguinte falhar, o aplicativo envia exatamente um comando de recuperação Dell Auto. A solicitação original continua com falha e não é repetida.
- `0x00-0x05` são seletores de destino do firmware, não porcentagens. O controle individual fica desativado por padrão; `0x00` acelerou todos os ventiladores no servidor testado.
- Comandos do usuário aguardam o bloqueio IPMI. Consultas e ciclos automáticos em segundo plano são ignorados de forma visível quando o bloqueio está ocupado; um segundo processo `ipmitool` não é iniciado.
- Velocidades baixas ou curvas incorretas podem superaquecer CPU, discos, placas PCIe e fontes. Use Dell Auto em caso de dúvida.

## Verificação e documentação

```powershell
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj -c Release
dotnet build .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

Essas verificações não substituem testes no servidor real. Inicialização da GUI, comandos raw reais, outros firmwares iDRAC e todas as combinações DPI não estão totalmente cobertos. Consulte o [guia em inglês](README.en-US.md), os [comandos IPMI](docs/COMMANDS.en-US.md) e a [segurança](SECURITY.en-US.md).
