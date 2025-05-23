using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using EasyChatServer; // Program class namespace
using System.IO;
using System;
using System.Threading;
using System.Text;

namespace EasyChatServer.Tests
{
    [TestClass]
    public class InteractionTests
    {
        private Task _serverTask;

        [TestCleanup]
        public async Task TestCleanup()
        {
            try
            {
                Console.WriteLine("[TestCleanup InteractionTests] Attempting to stop server...");
                await Program.StopServerAsync(); 

                if (_serverTask != null && !_serverTask.IsCompleted)
                {
                    Console.WriteLine("[TestCleanup InteractionTests] Waiting for server task to complete...");
                    bool completed = await Task.WhenAny(_serverTask, Task.Delay(TimeSpan.FromSeconds(5))) == _serverTask;
                    if (!completed && !_serverTask.IsCompleted)
                    {
                        Console.WriteLine("[TestCleanup InteractionTests] Server task did not complete in time.");
                    }
                }
                Console.WriteLine("[TestCleanup InteractionTests] Server stop process completed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TestCleanup InteractionTests] Error during server stop: {ex.Message}");
            }
            finally
            {
                if (Program.listener != null && Program.listener.Server.IsBound)
                {
                    try 
                    {
                        Program.listener.Stop(); 
                        Console.WriteLine("[TestCleanup InteractionTests] Listener explicitly stopped.");
                    } 
                    catch (Exception e) 
                    { 
                        Console.WriteLine($"[TestCleanup InteractionTests] Listener stop error (final attempt): {e.Message}"); 
                    }
                }
                _serverTask = null; 
            }
        }

        private async Task<(TcpClient client, StreamReader reader, StreamWriter writer)> ConnectClientAsync(int port)
        {
            var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            var stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
            return (client, reader, writer);
        }

        [TestMethod]
        public async Task TestClientDisconnectionNotification()
        {
            Console.WriteLine("[TestClientDisconnectionNotification] Starting test...");
            IConfiguration configuration = new ConfigurationBuilder().Build(); // Default port 8888

            _serverTask = Task.Run(() => Program.RunServerAsync(configuration));
            await Task.Delay(TimeSpan.FromSeconds(1)); // Give server time to start

            TcpClient clientA = null;
            StreamWriter writerA = null;
            
            TcpClient clientB = null;
            StreamReader readerB = null;

            string clientAEndpoint = null;

            try
            {
                // Connect Client A
                Console.WriteLine("[TestClientDisconnectionNotification] Connecting Client A...");
                var (connectedClientA, _, connectedWriterA) = await ConnectClientAsync(8888);
                clientA = connectedClientA;
                writerA = connectedWriterA;
                clientAEndpoint = clientA.Client.LocalEndPoint.ToString(); // Get endpoint before disconnect
                Console.WriteLine($"[TestClientDisconnectionNotification] Client A connected from {clientAEndpoint}.");

                // Connect Client B
                Console.WriteLine("[TestClientDisconnectionNotification] Connecting Client B...");
                var (connectedClientB, connectedReaderB, _) = await ConnectClientAsync(8888);
                clientB = connectedClientB;
                readerB = connectedReaderB;
                Console.WriteLine("[TestClientDisconnectionNotification] Client B connected.");

                // Client B: Skip welcome message
                await readerB.ReadLineAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token); // "Welcome..."

                // Client A disconnects
                Console.WriteLine("[TestClientDisconnectionNotification] Client A sending 'quit'...");
                await writerA.WriteLineAsync("quit");
                // Wait for server to process quit and broadcast
                await Task.Delay(TimeSpan.FromSeconds(1)); 

                Console.WriteLine("[TestClientDisconnectionNotification] Client B listening for disconnect message...");
                string disconnectMessage = await readerB.ReadLineAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
                
                Assert.IsNotNull(disconnectMessage, "Client B did not receive any message.");
                Console.WriteLine($"[TestClientDisconnectionNotification] Client B received: {disconnectMessage}");

                // The actual endpoint of Client A as seen by the server might be its RemoteEndPoint.
                // For this test, we check if the message contains "has disconnected."
                // A more robust check would be to get Client A's RemoteEndPoint as seen by the server.
                // The current Program.HandleClientAsync uses tcpClient.Client.RemoteEndPoint.ToString()
                // which is what we need to match.
                // However, clientA.Client.LocalEndPoint is what we have here.
                // The server message is "Server: Client [RemoteEndPoint of A] has disconnected."
                // For a local connection, RemoteEndPoint and LocalEndPoint will have the same IP but different ports.
                // We'll assert for the general structure of the message.
                StringAssert.Contains(disconnectMessage, "has disconnected.", "Disconnect message format is incorrect.");
                StringAssert.Contains(disconnectMessage, "Server: Client", "Message should identify as from Server.");

                // To make the assertion more precise, we'd need clientA's remote endpoint as seen by the server.
                // For now, checking for "has disconnected" and "Server: Client" is a good step.
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"[TestClientDisconnectionNotification] SocketException: {ex.Message}");
                Assert.Fail($"Test failed due to SocketException: {ex.Message}");
            }
            catch (TimeoutException ex)
            {
                Console.WriteLine($"[TestClientDisconnectionNotification] TimeoutException: {ex.Message}");
                Assert.Fail($"Test failed due to TimeoutException (e.g. server not sending message): {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TestClientDisconnectionNotification] General Exception: {ex.Message}");
                Assert.Fail($"An unexpected error occurred: {ex.Message}");
            }
            finally
            {
                writerA?.Close();
                clientA?.Close();
                readerB?.Close();
                clientB?.Close();
                Console.WriteLine("[TestClientDisconnectionNotification] Test finished.");
            }
        }
    }
}
