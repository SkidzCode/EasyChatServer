using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using EasyChatServer; // Program class namespace
using System.IO;
using System;
using System.Threading;

namespace EasyChatServer.Tests
{
    [TestClass]
    public class PortConfigurationTests
    {
        private Task _serverTask;
        
        [TestCleanup]
        public async Task TestCleanup()
        {
            try
            {
                Console.WriteLine("[TestCleanup] Attempting to stop server...");
                await Program.StopServerAsync(); 

                if (_serverTask != null && !_serverTask.IsCompleted)
                {
                    Console.WriteLine("[TestCleanup] Waiting for server task to complete...");
                    // Give the server task a chance to complete after cancellation.
                    bool completed = await Task.WhenAny(_serverTask, Task.Delay(TimeSpan.FromSeconds(5))) == _serverTask;
                    if (!completed && !_serverTask.IsCompleted)
                    {
                        Console.WriteLine("[TestCleanup] Server task did not complete in time after StopServerAsync. It might be stuck or already completed.");
                        // If Program.cancellationTokenSource is static readonly, we can't replace it.
                        // StopServerAsync should have cancelled it. If the task is still running,
                        // it might be an issue in AcceptClientsAsync not responding to cancellation.
                    }
                }
                Console.WriteLine("[TestCleanup] Server stop process completed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TestCleanup] Error during server stop: {ex.Message}");
            }
            finally
            {
                // Additional check to ensure listener is stopped, as StopServerAsync might have issues.
                if (Program.listener != null && Program.listener.Server.IsBound)
                {
                    try 
                    {
                        Program.listener.Stop(); 
                        Console.WriteLine("[TestCleanup] Listener explicitly stopped.");
                    } 
                    catch (Exception e) 
                    { 
                        Console.WriteLine($"[TestCleanup] Listener stop error (final attempt): {e.Message}"); 
                    }
                }
                _serverTask = null; 
            }
        }

        [TestMethod]
        public async Task TestDefaultPort()
        {
            Console.WriteLine("[TestDefaultPort] Starting test...");
            IConfiguration configuration = new ConfigurationBuilder().Build(); 

            // Resetting the CancellationTokenSource for Program if it's static and has been cancelled.
            // This is complex because Program.cancellationTokenSource is internal static readonly.
            // The design of Program.cs doesn't allow easy reset for tests.
            // We rely on StopServerAsync in TestCleanup to have cancelled the token,
            // and RunServerAsync should ideally work with an already cancelled token by not starting,
            // or by using a new token if it were designed to accept one.
            // For now, we assume that StopServerAsync will cancel the token and RunServerAsync
            // will restart the listener with a fresh AcceptClientsAsync call that respects the token.
            // If Program.cancellationTokenSource is cancelled and not reset, subsequent tests might fail to start the server.
            // The current Program.cs structure (static CTS) is problematic for sequential tests in the same process.
            // A possible "fix" if tests fail sequentially is to ensure Program.cancellationTokenSource is re-instantiated,
            // but that requires changing its 'readonly' nature or providing a static reset method.
            // Let's assume for now StopServerAsync and RunServerAsync manage this.

            _serverTask = Task.Run(() => Program.RunServerAsync(configuration));
            
            TcpClient client = null;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2)); 
                client = new TcpClient();
                Console.WriteLine("[TestDefaultPort] Attempting to connect to 127.0.0.1:8888...");
                await client.ConnectAsync("127.0.0.1", 8888);
                Assert.IsTrue(client.Connected, "Client should be connected to the default port 8888.");
                Console.WriteLine("[TestDefaultPort] Connected successfully.");
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"[TestDefaultPort] SocketException: {ex.Message}");
                Assert.Fail($"Connection to default port 8888 failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TestDefaultPort] General Exception: {ex.Message}");
                Assert.Fail($"An unexpected error occurred: {ex.Message}");
            }
            finally
            {
                client?.Close();
                Console.WriteLine("[TestDefaultPort] Test finished.");
            }
        }

        [TestMethod]
        public async Task TestCustomPort()
        {
            Console.WriteLine("[TestCustomPort] Starting test...");
            IConfiguration configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory) 
                .AddJsonFile("appsettings.TestCustomPort.json", optional: false, reloadOnChange: true)
                .Build();

            _serverTask = Task.Run(() => Program.RunServerAsync(configuration));

            TcpClient client = null;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2)); 
                client = new TcpClient();
                Console.WriteLine("[TestCustomPort] Attempting to connect to 127.0.0.1:9999...");
                await client.ConnectAsync("127.0.0.1", 9999);
                Assert.IsTrue(client.Connected, "Client should be connected to the custom port 9999.");
                Console.WriteLine("[TestCustomPort] Connected successfully.");
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"[TestCustomPort] SocketException: {ex.Message}");
                Assert.Fail($"Connection to custom port 9999 failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TestCustomPort] General Exception: {ex.Message}");
                Assert.Fail($"An unexpected error occurred: {ex.Message}");
            }
            finally
            {
                client?.Close();
                Console.WriteLine("[TestCustomPort] Test finished.");
            }
        }
    }
}
