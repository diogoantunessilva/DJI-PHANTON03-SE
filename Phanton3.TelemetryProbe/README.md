# Phanton3.TelemetryProbe

Aplicação Console para Windows, em C# e .NET 8, que captura somente os bytes recebidos por TCP de duas fontes pela placa **Wi-Fi** conectada ao DJI: `controller_2345` (`192.168.1.1:2345`) e `aircraft_5678` (`192.168.1.2:5678`). As duas conexões são independentes e usam timeout de conexão de 5 segundos e reconexão automática. A placa **Ethernet** continua destinada à internet. O programa não envia dados ou comandos ao equipamento.

O fluxo TCP é remontado em quadros DUML v1. O programa extrai os campos do cabeçalho, o payload em hexadecimal e valida CRC8 (seed `0x77`) e CRC16 (seed `0x3692`). Quadros completos com CRC inválido também aparecem no terminal e no CSV, com `CRC8_OK` e `CRC16_OK` indicando a falha.

Na fonte do controlador, quadros válidos de canais RC (`cmdSet=0x06`, `cmdId=0x05`, payload de 13 bytes) são exibidos com valor `UInt16` bruto, porcentagem e barra para Aileron, Elevator, Throttle, Rudder e GyroValue. A porcentagem é uma referência visual baseada na faixa observada no teste controlado: `364 = -100%`, `1024 = 0%`, `1684 = +100%`, limitada a ±100%. O CSV de canais RC permanece bruto. Roda, status e bits de botões/modo continuam visíveis. Também são identificados RSSI bruto (`0x07/0x09`), status bruto do sinal Wi-Fi (`0x07/0x12`), `RC Battery Info` (`0x06/0x1E`, payload mantido bruto) e `Heartbeat` (`0x00/0x0E`). Quadros com CRC inválido permanecem nos arquivos DUML, mas não geram interpretação semântica. A fonte da aeronave reutiliza o mesmo parser DUML e não faz interpretação semântica de seus quadros.

## Executar

Com o SDK do .NET 8 instalado e a rede do equipamento acessível:

```powershell
cd Phanton3.TelemetryProbe
dotnet run
```

Se o executável atualizado já estiver compilado, não é necessário conhecer C# nem instalar o SDK para executá-lo:

```powershell
.\publish-rc-display\Phanton3.TelemetryProbe.exe
```

## Wi-Fi para telemetria DJI e Ethernet para internet

Neste computador, Ethernet e Wi-Fi estão na rede `192.168.1.x`, com gateways diferentes que usam o mesmo IP `192.168.1.1`. O programa seleciona o IPv4 da placa chamada `Wi-Fi` a cada conexão; se o DHCP mudar esse endereço, não é preciso editar o código. O log mostra qual IP local foi selecionado.

No momento, o Windows já dá prioridade de navegação à Ethernet (métrica 25, contra 55 do Wi-Fi). Portanto, primeiro teste apenas o programa atualizado. Se uma mudança de rede fizer o Windows priorizar o Wi-Fi para a internet, execute **uma vez** em um PowerShell aberto como **administrador**, na pasta do projeto:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Configurar-Rede.ps1
```

O script salva as prioridades atuais em `.network-metrics-backup.json`, configura Ethernet com métrica 10 e Wi-Fi com métrica 100. Para restaurar as prioridades anteriores, use `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Configurar-Rede.ps1 -Acao Restaurar` no PowerShell como administrador. O script não desativa nenhuma placa.

Feche a instância antiga com `Ctrl+C` e execute o programa atualizado. A configuração opcional altera a preferência geral de rede do Windows; o programa prende apenas sua conexão TCP ao Wi-Fi.

Os caminhos são relativos ao diretório de trabalho usado para iniciar o programa:

- `captures/controller_2345.bin`: bytes brutos, acrescentados ao arquivo a cada execução e reconexão.
- `logs/telemetry.log`: eventos, timestamp local com fuso, quantidade de bytes e prévia hexadecimal dos primeiros 32 bytes de cada leitura.
- `captures/duml_frames.bin`: quadros completos em sequência, inclusive os que falharam no CRC.
- `logs/duml_frames.csv`: campos de cada quadro, payload em hexadecimal, CRC16 recebido e colunas `CRC8_OK`/`CRC16_OK`.
- `logs/commands_summary.csv`: contagem acumulada de quadros completos por `sender,receiver,cmdSet,cmdId`, inclusive os que falharam no CRC. É atualizada durante a captura e ao encerrar.
- `logs/rc_channels.csv`: horário e valores brutos de aileron, elevator, throttle, rudder, gyro, wheel, status1 e status2, somente de quadros com CRC válido.
- `captures/aircraft_5678.bin`: todos os bytes brutos recebidos da aeronave, acrescentados entre execuções.
- `captures/aircraft_duml_frames.bin`: quadros DUML completos da aeronave, inclusive os com CRC inválido.
- `logs/aircraft_telemetry.log`: conexão, desconexão, quantidade de bytes e prévia hexadecimal da aeronave.
- `logs/aircraft_frames.csv`: quadros da aeronave com `timestamp,source,length,sender,receiver,sequence,flags,cmdSet,cmdId,payloadLength,payloadHex,crc8Ok,crc16Ok`, acrescentados entre execuções.
- `logs/aircraft_commands_summary.csv`: resumo da **execução atual** por `sender,receiver,cmdSet,cmdId`, com quantidade, comprimento do payload, frequência aproximada em Hz e primeiro/último timestamp. A frequência usa `(quantidade - 1) / (último - primeiro)`; para uma única ocorrência, é zero. Se um comando tiver comprimentos diferentes, a coluna `payloadLength` os separa com `|`. O resumo é atualizado durante a captura e ao encerrar.

## Teste controlado dos controles RC

Com o drone no chão e as hélices removidas, encerre qualquer instância anterior com `Ctrl+C` e execute:

```powershell
.\publish-rc-display\Phanton3.TelemetryProbe.exe --rc-test
```

O programa espera receber um quadro RC válido e pede **Enter** para começar. Não mova os controles antes de pressionar Enter. Ele marca os intervalos no terminal e em `logs/rc_test_markers_*.csv`, depois encerra a captura com segurança aos 60 segundos:

| Intervalo | Ação |
| --- | --- |
| 00–10 s | Tudo parado |
| 10–20 s | Stick direito esquerda/direita |
| 20–30 s | Stick direito cima/baixo |
| 30–40 s | Stick esquerdo cima/baixo |
| 40–50 s | Stick esquerdo esquerda/direita |
| 50–60 s | Roda do gimbal |

Durante esse modo, as linhas de todos os quadros ficam ocultas no terminal para deixar os avisos de tempo visíveis. Os arquivos de captura e CSV continuam sendo gravados. O programa não envia dados ao drone em nenhum modo.

Pressione `Ctrl+C` para fechar a conexão e descarregar os arquivos. A tentativa de conexão expira em 5 segundos; após uma falha ou desconexão, o programa tenta novamente em 3 segundos.

Cada linha de quadro no terminal indica `source=controller_2345` ou `source=aircraft_5678`. Para adicionar outra fonte futuramente, inclua outra entrada em `sources` no `Program.cs`, com seus próprios caminhos de captura e log. Um buffer de quadro parcial é descartado após cada desconexão, sem misturar bytes de conexões diferentes. Esta versão não implementa comandos, ACKs, heartbeat, controle de voo ou escrita no socket.

## Referências do formato

- [Cabeçalho DUML v1 em dji-firmware-tools](https://github.com/o-gs/dji-firmware-tools/blob/master/comm_mkdupc.py)
- [Framing e CRCs em dji-firmware-tools](https://github.com/o-gs/dji-firmware-tools/blob/master/comm_dat2pcap.py)
