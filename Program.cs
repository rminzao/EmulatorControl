using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EmulatorControl
{
    class Program
    {
        private static readonly string EMULATOR_PATH = Environment.GetEnvironmentVariable("EMULATOR_PATH") 
            ?? @"C:\v5500\Emulador";
        
        private static readonly int PORT = int.TryParse(Environment.GetEnvironmentVariable("EMULATOR_PORT"), out int port) 
            ? port : 8989;
        
        private static HttpListener listener;

        static async Task Main(string[] args)
        {
            Console.Title = "Emulator Control API - VPS";
            
            // Verificar se está executando como Admin
            if (!IsRunningAsAdmin())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("❌ ERRO: Execute este programa como Administrador!");
                Console.WriteLine("Clique com botão direito -> 'Executar como administrador'");
                Console.ReadKey();
                return;
            }

            // Verificar se a pasta dos emuladores existe
            if (!Directory.Exists(EMULATOR_PATH))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ ERRO: Pasta não encontrada: {EMULATOR_PATH}");
                Console.WriteLine();
                Console.WriteLine("💡 Configure a variável de ambiente EMULATOR_PATH");
                Console.WriteLine("   Exemplo: set EMULATOR_PATH=D:\\GameServer\\Emulators");
                Console.ReadKey();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("🎮 EMULATOR CONTROL API");
            Console.WriteLine("=======================");
            Console.WriteLine($"📁 Pasta: {EMULATOR_PATH}");
            Console.WriteLine($"🌐 Porta: {PORT}");
            Console.WriteLine($"✅ Executando como Administrador");
            
            // Mostrar configuração de ambiente
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine();
            Console.WriteLine("📋 Configuração:");
            Console.WriteLine($"   EMULATOR_PATH = {EMULATOR_PATH}");
            Console.WriteLine($"   EMULATOR_PORT = {PORT}");
            Console.WriteLine();

            await StartServer();
        }

        static async Task StartServer()
        {
            listener = new HttpListener();
            listener.Prefixes.Add($"http://+:{PORT}/");
            
            try
            {
                listener.Start();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"🚀 Servidor iniciado em http://localhost:{PORT}");
                Console.WriteLine();
                Console.WriteLine("Endpoints disponíveis:");
                Console.WriteLine($"  POST http://localhost:{PORT}/start   - Iniciar emuladores");
                Console.WriteLine($"  POST http://localhost:{PORT}/stop    - Parar emuladores");
                Console.WriteLine($"  GET  http://localhost:{PORT}/status  - Ver status");
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Aguardando comandos do painel... (Ctrl+C para sair)");
                Console.WriteLine();

                while (true)
                {
                    var context = await listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context));
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ Erro ao iniciar servidor: {ex.Message}");
                if (ex.Message.Contains("Access is denied"))
                {
                    Console.WriteLine("💡 Solução: Execute como Administrador ou libere a porta no firewall");
                }
                Console.ReadKey();
            }
        }

        static async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            
            // Configurar CORS para permitir requisições do painel PHP
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            try
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"📥 {DateTime.Now:HH:mm:ss} - {request.HttpMethod} {request.Url.AbsolutePath}");

                string responseText = "";
                
                switch (request.Url.AbsolutePath.ToLower())
                {
                    case "/start":
                        responseText = await StartEmulators();
                        break;
                    case "/stop":
                        responseText = StopEmulators();
                        break;
                    case "/status":
                        responseText = GetStatus();
                        break;
                    case "/":
                        responseText = GetHelp();
                        break;
                    default:
                        response.StatusCode = 404;
                        responseText = JsonSerializer.Serialize(new { error = "Endpoint não encontrado" });
                        break;
                }

                byte[] buffer = Encoding.UTF8.GetBytes(responseText);
                response.ContentType = "application/json";
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ Erro ao processar requisição: {ex.Message}");
                
                response.StatusCode = 500;
                string errorResponse = JsonSerializer.Serialize(new { error = ex.Message });
                byte[] buffer = Encoding.UTF8.GetBytes(errorResponse);
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            finally
            {
                response.Close();
            }
        }

        static async Task<string> StartEmulators()
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("🚀 Iniciando sequência de emuladores...");

            var results = new List<object>();
            var emulators = new[]
            {
                new { Name = "Center", Exe = "Center.service.exe", Process = "Center.Service", Delay = 3000 },
                new { Name = "Fighting", Exe = "Fighting.Service.exe", Process = "Fighting.Service", Delay = 15000 },
                new { Name = "Road", Exe = "Road.Service.exe", Process = "Road.Service", Delay = 0 }
            };

            foreach (var emu in emulators)
            {
                try
                {
                    // Verificar se já está rodando
                    if (IsProcessRunning(emu.Process))
                    {
                        Console.WriteLine($"⚠️  {emu.Name} já está rodando");
                        results.Add(new { emulator = emu.Name, status = "already_running" });
                        continue;
                    }

                    // Iniciar emulador
                    string exePath = Path.Combine(EMULATOR_PATH, emu.Exe);
                    
                    if (!File.Exists(exePath))
                    {
                        Console.WriteLine($"❌ {emu.Name} - Arquivo não encontrado: {exePath}");
                        results.Add(new { emulator = emu.Name, status = "file_not_found" });
                        continue;
                    }

                    var processInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        WorkingDirectory = EMULATOR_PATH,
                        UseShellExecute = true,
                        Verb = "runas" // Executar como admin
                    };

                    Process.Start(processInfo);
                    Console.WriteLine($"✅ {emu.Name} iniciado");
                    results.Add(new { emulator = emu.Name, status = "started" });

                    // Aguardar delay
                    if (emu.Delay > 0)
                    {
                        Console.WriteLine($"⏱️  Aguardando {emu.Delay/1000}s...");
                        await Task.Delay(emu.Delay);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Erro ao iniciar {emu.Name}: {ex.Message}");
                    results.Add(new { emulator = emu.Name, status = "error", message = ex.Message });
                }
            }

            Console.WriteLine("🏁 Sequência concluída!");
            return JsonSerializer.Serialize(new { success = true, message = "Sequência de inicialização concluída", results });
        }

        static string StopEmulators()
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("🛑 Parando emuladores...");

            var results = new List<object>();
            var processes = new[] { "Road.Service", "Fighting.Service", "Center.Service" }; // Ordem reversa

            foreach (var processName in processes)
            {
                try
                {
                    if (IsProcessRunning(processName))
                    {
                        var processes_found = Process.GetProcessesByName(processName);
                        foreach (var proc in processes_found)
                        {
                            proc.Kill();
                            proc.WaitForExit(5000); // Aguarda 5s
                        }
                        Console.WriteLine($"✅ {processName} parado");
                        results.Add(new { emulator = processName, status = "stopped" });
                    }
                    else
                    {
                        Console.WriteLine($"⚠️  {processName} não estava rodando");
                        results.Add(new { emulator = processName, status = "not_running" });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Erro ao parar {processName}: {ex.Message}");
                    results.Add(new { emulator = processName, status = "error", message = ex.Message });
                }
            }

            Console.WriteLine("🏁 Todos os emuladores foram parados!");
            return JsonSerializer.Serialize(new { success = true, message = "Emuladores parados", results });
        }

        static string GetStatus()
        {
            var status = new List<object>();
            var emulators = new[]
            {
                new { Name = "Center", Process = "Center.Service" },
                new { Name = "Fighting", Process = "Fighting.Service" },
                new { Name = "Road", Process = "Road.Service" }
            };

            foreach (var emu in emulators)
            {
                try
                {
                    var processes = Process.GetProcessesByName(emu.Process);
                    var isRunning = processes.Length > 0;
                    
                    if (isRunning)
                    {
                        var proc = processes[0];
                        status.Add(new
                        {
                            name = emu.Name,
                            process = emu.Process,
                            isRunning = true,
                            pid = proc.Id,
                            memoryMB = Math.Round(proc.WorkingSet64 / (1024.0 * 1024.0), 1),
                            startTime = proc.StartTime
                        });
                    }
                    else
                    {
                        status.Add(new
                        {
                            name = emu.Name,
                            process = emu.Process,
                            isRunning = false
                        });
                    }
                }
                catch (Exception ex)
                {
                    status.Add(new
                    {
                        name = emu.Name,
                        process = emu.Process,
                        isRunning = false,
                        error = ex.Message
                    });
                }
            }

            return JsonSerializer.Serialize(new { 
                timestamp = DateTime.Now, 
                emulators = status,
                config = new {
                    emulatorPath = EMULATOR_PATH,
                    port = PORT
                }
            });
        }

        static string GetHelp()
        {
            return JsonSerializer.Serialize(new 
            { 
                service = "Emulator Control API",
                version = "1.1",
                config = new {
                    emulatorPath = EMULATOR_PATH,
                    port = PORT,
                    envVars = new {
                        EMULATOR_PATH = "Caminho dos emuladores (padrão: C:\\v5500\\Emulador)",
                        EMULATOR_PORT = "Porta da API (padrão: 8989)"
                    }
                },
                endpoints = new 
                {
                    start = "POST /start - Iniciar emuladores em sequência",
                    stop = "POST /stop - Parar todos os emuladores",
                    status = "GET /status - Ver status atual"
                },
                sequence = "Center (3s) → Fighting (15s) → Road"
            });
        }

        static bool IsRunningAsAdmin()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        static bool IsProcessRunning(string processName)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                return processes.Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}