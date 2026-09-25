# Phanton3.TelemetryProbe

Aplicação Console para Windows, em C# e .NET 8, que captura somente os bytes recebidos por TCP de `192.168.1.1:2345`. Ela não envia dados ou comandos ao equipamento.

## Executar

Com o SDK do .NET 8 instalado e a rede do equipamento acessível:

```powershell
cd Phanton3.TelemetryProbe
dotnet run
```

Os caminhos são relativos ao diretório de trabalho usado para iniciar o programa:

- `captures/controller_2345.bin`: bytes brutos, acrescentados ao arquivo a cada execução e reconexão.
- `logs/telemetry.log`: eventos, timestamp local com fuso, quantidade de bytes e prévia hexadecimal dos primeiros 32 bytes de cada leitura.

Pressione `Ctrl+C` para fechar a conexão e descarregar os arquivos. A tentativa de conexão expira em 5 segundos; após uma falha ou desconexão, o programa tenta novamente em 3 segundos.

Para adicionar outra fonte futuramente, inclua outra entrada em `sources` no `Program.cs`, com seus próprios caminhos de captura e log. Não há parser de protocolo nesta versão.
