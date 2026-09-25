# Phanton3.TelemetryProbe

Aplicação Console para Windows, em C# e .NET 8, que captura somente os bytes recebidos por TCP de `192.168.1.1:2345` pela placa **Ethernet**. Ela não envia dados ou comandos ao equipamento.

## Executar

Com o SDK do .NET 8 instalado e a rede do equipamento acessível:

```powershell
cd Phanton3.TelemetryProbe
dotnet run
```

Se o executável já estiver compilado, não é necessário conhecer C# nem instalar o SDK para executá-lo:

```powershell
.\bin\Release\net8.0\Phanton3.TelemetryProbe.exe
```

## Ethernet para telemetria e Wi-Fi para internet

Neste computador, Ethernet e Wi-Fi estão na rede `192.168.1.x`, com gateways diferentes que usam o mesmo IP `192.168.1.1`. O programa seleciona o IPv4 da placa chamada `Ethernet` a cada conexão; se o DHCP mudar esse endereço, não é preciso editar o código. O log mostra qual IP local foi selecionado.

Para dar prioridade de navegação ao Wi-Fi, execute **uma vez** em um PowerShell aberto como **administrador**, na pasta do projeto:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Configurar-Rede.ps1
```

O script salva as prioridades atuais em `.network-metrics-backup.json`, configura Wi-Fi com métrica 10 e Ethernet com métrica 100. Para restaurar as prioridades anteriores, use `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Configurar-Rede.ps1 -Acao Restaurar` no PowerShell como administrador. O script não desativa nenhuma placa.

Depois, feche a instância antiga com `Ctrl+C` e execute o programa atualizado. A configuração altera a preferência geral de rede do Windows; o programa continua prendendo apenas sua conexão TCP à Ethernet.

Os caminhos são relativos ao diretório de trabalho usado para iniciar o programa:

- `captures/controller_2345.bin`: bytes brutos, acrescentados ao arquivo a cada execução e reconexão.
- `logs/telemetry.log`: eventos, timestamp local com fuso, quantidade de bytes e prévia hexadecimal dos primeiros 32 bytes de cada leitura.

Pressione `Ctrl+C` para fechar a conexão e descarregar os arquivos. A tentativa de conexão expira em 5 segundos; após uma falha ou desconexão, o programa tenta novamente em 3 segundos.

Para adicionar outra fonte futuramente, inclua outra entrada em `sources` no `Program.cs`, com seus próprios caminhos de captura e log. Não há parser de protocolo nesta versão.
