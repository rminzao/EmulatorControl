using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EmulatorControl
{
    public class EmulatorConfig
    {
        public int Port { get; set; }
        public List<ServerConfig> Servers { get; set; } = new();
    }

    public class ServerConfig
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public bool Enabled { get; set; }
        public List<EmulatorInfo> Emulators { get; set; } = new();
    }

    public class EmulatorInfo
    {
        public string Name { get; set; } = "";
        public string Exe { get; set; } = "";
        public string Process { get; set; } = "";
        public int Delay { get; set; }
    }

    class Program
    {
        private static EmulatorConfig config;
        private static HttpListener listener;
        private static readonly string CONFIG_FILE = "emulators.json";

        static async Task Main(string[] args)
        {
            Console.Title = "Emulator Control API - Multi Server VPS";
            
            // Verificar se está executando como Admin
            if (!IsRunningAsAdmin())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("ERRO: Execute este programa como Administrador!");
                Console.WriteLine("Clique com botão direito -> 'Executar como administrador'");
                Console.ReadKey();
                return;
            }

            // Carregar configuração
            if (!LoadConfig())
            {
                Console.ReadKey();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("🎮 EMULATOR CONTROL API");
            Console.WriteLine("========================================");
            Console.WriteLine($"🌐 Porta: {config.Port}");
            Console.WriteLine($"✅ Executando como Administrador");
            Console.WriteLine();

            // Mostrar servidores configurados
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"📋 Servidores Configurados ({config.Servers.Count}):");
            foreach (var server in config.Servers)
            {
                string status = server.Enabled ? "✅ Habilitado" : "⚠️  Desabilitado";
                Console.WriteLine($"   [{server.Id}] {server.Name}");
                Console.WriteLine($"      Pasta: {server.Path}");
                Console.WriteLine($"      Status: {status}");
                Console.WriteLine($"      Emuladores: {server.Emulators.Count}");
                Console.WriteLine();
            }

            await StartServer();
        }

        static bool LoadConfig()
        {
            try
            {
                if (!File.Exists(CONFIG_FILE))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"❌ ERRO: Arquivo {CONFIG_FILE} não encontrado!");
                    Console.WriteLine();
                    Console.WriteLine("Crie o arquivo emulators.json com a configuração dos servidores.");
                    return false;
                }

                string json = File.ReadAllText(CONFIG_FILE);
                config = JsonSerializer.Deserialize<EmulatorConfig>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                if (config == null || config.Servers.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    return false;
                }

                // Validar pastas
                foreach (var server in config.Servers)
                {
                    if (server.Enabled && !Directory.Exists(server.Path))
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"⚠️  AVISO: Pasta não encontrada para {server.Name}: {server.Path}");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ Erro ao carregar configuração: {ex.Message}");
                return false;
            }
        }

        static async Task StartServer()
        {
            listener = new HttpListener();
            listener.Prefixes.Add($"http://+:{config.Port}/");
            
            try
            {
                listener.Start();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"🚀 Servidor iniciado em http://localhost:{config.Port}");
                Console.WriteLine();
                Console.WriteLine("Endpoints disponíveis:");
                Console.WriteLine($"  POST http://localhost:{config.Port}/start/{{serverId}}  - Iniciar emuladores de um servidor");
                Console.WriteLine($"  POST http://localhost:{config.Port}/start/all          - Iniciar TODOS os emuladores");
                Console.WriteLine($"  POST http://localhost:{config.Port}/stop/{{serverId}}   - Parar emuladores de um servidor");
                Console.WriteLine($"  POST http://localhost:{config.Port}/stop/all           - Parar TODOS os emuladores");
                Console.WriteLine($"  GET  http://localhost:{config.Port}/status            - Ver status de todos");
                Console.WriteLine($"  GET  http://localhost:{config.Port}/status/{{serverId}} - Ver status de um servidor");
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
                    Console.WriteLine("Execute como Administrador ou libere a porta no firewall");
                }
                Console.ReadKey();
            }
        }

        static async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            
            // Configurar CORS
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            try
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"📥 {DateTime.Now:HH:mm:ss} - {request.HttpMethod} {request.Url.AbsolutePath}");

                string responseText = "";
                string path = request.Url.AbsolutePath.ToLower();
                string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

                if (path == "/" || path == "")
                {
                    responseText = GetHelp();
                }
                else if (segments.Length >= 1 && segments[0] == "start")
                {
                    string serverId = segments.Length > 1 ? segments[1] : "all";
                    responseText = await StartEmulators(serverId);
                }
                else if (segments.Length >= 1 && segments[0] == "stop")
                {
                    string serverId = segments.Length > 1 ? segments[1] : "all";
                    responseText = StopEmulators(serverId);
                }
                else if (segments.Length >= 1 && segments[0] == "status")
                {
                    string serverId = segments.Length > 1 ? segments[1] : null;
                    responseText = GetStatus(serverId);
                }
                else
                {
                    response.StatusCode = 404;
                    responseText = JsonSerializer.Serialize(new { error = "Endpoint não encontrado" });
                }

                byte[] buffer = Encoding.UTF8.GetBytes(responseText);
                response.ContentType = "application/json";
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
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

        static async Task<string> StartEmulators(string serverId)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            
            List<ServerConfig> serversToStart;
            
            if (serverId == "all")
            {
                serversToStart = config.Servers.Where(s => s.Enabled).ToList();
            }
            else
            {
                var server = config.Servers.FirstOrDefault(s => s.Id == serverId);
                if (server == null)
                {
                    return JsonSerializer.Serialize(new { success = false, error = $"Servidor '{serverId}' não encontrado" });
                }
                if (!server.Enabled)
                {
                    return JsonSerializer.Serialize(new { success = false, error = $"Servidor '{serverId}' está desabilitado" });
                }
                serversToStart = new List<ServerConfig> { server };
            }

            var allResults = new List<object>();

            foreach (var server in serversToStart)
            {   
                foreach (var emu in server.Emulators)
                {
                    try
                    {
                        if (IsProcessRunning(emu.Process, server.Path))
                        {
                            allResults.Add(new { 
                                server = server.Id, 
                                emulator = emu.Name, 
                                status = "already_running" 
                            });
                            continue;
                        }

                        // Iniciar emulador
                        string exePath = Path.Combine(server.Path, emu.Exe);
                        
                        if (!File.Exists(exePath))
                        {
                            allResults.Add(new { 
                                server = server.Id, 
                                emulator = emu.Name, 
                                status = "file_not_found",
                                path = exePath
                            });
                            continue;
                        }

                        var processInfo = new ProcessStartInfo
                        {
                            FileName = exePath,
                            WorkingDirectory = server.Path,
                            UseShellExecute = true,
                            Verb = "runas"
                        };

                        Process.Start(processInfo);
                        Console.WriteLine($"✅ {server.Id}/{emu.Name} iniciado");
                        allResults.Add(new { 
                            server = server.Id, 
                            emulator = emu.Name, 
                            status = "started" 
                        });

                        // Aguardar delay
                        if (emu.Delay > 0)
                        {
                            Console.WriteLine($"⏱️  Aguardando {emu.Delay/1000}s...");
                            await Task.Delay(emu.Delay);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Erro ao iniciar {server.Id}/{emu.Name}: {ex.Message}");
                        allResults.Add(new { 
                            server = server.Id, 
                            emulator = emu.Name, 
                            status = "error", 
                            message = ex.Message 
                        });
                    }
                }
            }

            return JsonSerializer.Serialize(new { 
                success = true, 
                message = "Sequência de inicialização concluída", 
                results = allResults 
            });
        }

        static string StopEmulators(string serverId)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            
            List<ServerConfig> serversToStop;
            
            if (serverId == "all")
            {
                serversToStop = config.Servers.Where(s => s.Enabled).ToList();
            }
            else
            {
                var server = config.Servers.FirstOrDefault(s => s.Id == serverId);
                if (server == null)
                {
                    return JsonSerializer.Serialize(new { success = false, error = $"Servidor '{serverId}' não encontrado" });
                }
                serversToStop = new List<ServerConfig> { server };
            }

            var allResults = new List<object>();

            foreach (var server in serversToStop)
            {   
                var emuReversed = server.Emulators.AsEnumerable().Reverse().ToList();
                
                foreach (var emu in emuReversed)
                {
                    try
                    {
                        if (IsProcessRunning(emu.Process, server.Path))
                        {
                            var processes = Process.GetProcessesByName(emu.Process);
                            
                            foreach (var proc in processes)
                            {
                                try
                                {
                                    string processPath = Path.GetDirectoryName(proc.MainModule.FileName);
                                    
                                    if (processPath != null && 
                                        processPath.Equals(server.Path, StringComparison.OrdinalIgnoreCase))
                                    {
                                        proc.Kill();
                                        proc.WaitForExit(5000);
                                        Console.WriteLine($"✅ {server.Id}/{emu.Name} parado");
                                    }
                                }
                                catch
                                {
                                    // Ignora erros de acesso
                                    continue;
                                }
                            }
                            
                            allResults.Add(new { 
                                server = server.Id, 
                                emulator = emu.Name, 
                                status = "stopped" 
                            });
                        }
                        else
                        {
                            Console.WriteLine($"⚠️  {server.Id}/{emu.Name} não estava rodando");
                            allResults.Add(new { 
                                server = server.Id, 
                                emulator = emu.Name, 
                                status = "not_running" 
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Erro ao parar {server.Id}/{emu.Name}: {ex.Message}");
                        allResults.Add(new { 
                            server = server.Id, 
                            emulator = emu.Name, 
                            status = "error", 
                            message = ex.Message 
                        });
                    }
                }
            }
            return JsonSerializer.Serialize(new { 
                success = true, 
                message = "Emuladores parados", 
                results = allResults 
            });
        }

        static string GetStatus(string serverId = null)
        {
            var allStatus = new List<object>();
            
            IEnumerable<ServerConfig> serversToCheck = serverId == null 
                ? config.Servers 
                : config.Servers.Where(s => s.Id == serverId);

            foreach (var server in serversToCheck)
            {
                var serverEmulators = new List<object>();
                
                foreach (var emu in server.Emulators)
                {
                    try
                    {
                        var processes = Process.GetProcessesByName(emu.Process);
                        bool isRunning = false;
                        Process matchingProcess = null;

                        foreach (var proc in processes)
                        {
                            try
                            {
                                string processPath = Path.GetDirectoryName(proc.MainModule.FileName);
                                
                                if (processPath != null && 
                                    processPath.Equals(server.Path, StringComparison.OrdinalIgnoreCase))
                                {
                                    isRunning = true;
                                    matchingProcess = proc;
                                    break;
                                }
                            }
                            catch
                            {
                                continue;
                            }
                        }
                        
                        if (isRunning && matchingProcess != null)
                        {
                            serverEmulators.Add(new
                            {
                                name = emu.Name,
                                process = emu.Process,
                                isRunning = true,
                                pid = matchingProcess.Id,
                                memoryMB = Math.Round(matchingProcess.WorkingSet64 / (1024.0 * 1024.0), 1),
                                startTime = matchingProcess.StartTime,
                                path = server.Path
                            });
                        }
                        else
                        {
                            serverEmulators.Add(new
                            {
                                name = emu.Name,
                                process = emu.Process,
                                isRunning = false,
                                path = server.Path
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        serverEmulators.Add(new
                        {
                            name = emu.Name,
                            process = emu.Process,
                            isRunning = false,
                            error = ex.Message,
                            path = server.Path
                        });
                    }
                }

                allStatus.Add(new
                {
                    serverId = server.Id,
                    serverName = server.Name,
                    serverPath = server.Path,
                    enabled = server.Enabled,
                    emulators = serverEmulators
                });
            }

            return JsonSerializer.Serialize(new { 
                timestamp = DateTime.Now,
                servers = allStatus
            });
        }

        static string GetHelp()
        {
            var serverList = config.Servers.Select(s => new { 
                id = s.Id, 
                name = s.Name, 
                path = s.Path, 
                enabled = s.Enabled,
                emulators = s.Emulators.Count
            }).ToList();

            return JsonSerializer.Serialize(new 
            { 
                service = "Emulator Control API - Multi Server",
                version = "2.0",
                port = config.Port,
                servers = serverList,
                endpoints = new 
                {
                    start_one = "POST /start/{serverId} - Iniciar emuladores de um servidor específico",
                    start_all = "POST /start/all - Iniciar TODOS os emuladores de todos servidores",
                    stop_one = "POST /stop/{serverId} - Parar emuladores de um servidor específico",
                    stop_all = "POST /stop/all - Parar TODOS os emuladores",
                    status_all = "GET /status - Ver status de todos os servidores",
                    status_one = "GET /status/{serverId} - Ver status de um servidor específico"
                },
                examples = new
                {
                    start_server1 = $"POST http://localhost:{config.Port}/start/s1",
                    stop_server2 = $"POST http://localhost:{config.Port}/stop/s2",
                    status = $"GET http://localhost:{config.Port}/status"
                }
            });
        }

        static bool IsRunningAsAdmin()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        static bool IsProcessRunning(string processName, string expectedPath)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                
                foreach (var proc in processes)
                {
                    try
                    {
                        string processPath = Path.GetDirectoryName(proc.MainModule.FileName);
                        
                        if (processPath != null && 
                            processPath.Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}