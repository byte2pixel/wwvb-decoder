# host

C# / .NET 10 solution (`WwvbDecoder.slnx`).

| Project | Purpose |
| --- | --- |
| `src/WwvbDecoder.Core` | Decoder pipeline. No UI or platform dependencies. |
| `src/WwvbDecoder.Host` | WPF app that hosts the React UI in WebView2 and owns the serial port. |
| `tests/WwvbDecoder.Core.Tests` | xUnit tests for Core. |

WebView2 isn't referenced yet. Add it when you start on the host:

```powershell
dotnet add src/WwvbDecoder.Host package Microsoft.Web.WebView2
```
