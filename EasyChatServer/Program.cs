using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace EasyChatServer
{
    public class Program
    {
        private static readonly ConcurrentDictionary<string, MyClient> clients = new();
        internal static TcpListener listener;
        internal static readonly CancellationTokenSource cancellationTokenSource = new();

        static async Task Main(string[] args)
        {
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            if (args.Length == 0)
            {
                // Run as server
                var serverRunTask = RunServerAsync(configuration);
                Console.WriteLine("Press ENTER to stop the server...");
                Console.ReadLine(); // This blocks the Main thread for normal execution
                
                await StopServerAsync(); // Gracefully stop the server
                await serverRunTask; // Wait for server task to complete processing
                Console.WriteLine("Server stopped.");
            }
            else
            {
                // Run as client
                await RunClientAsync(args[0], configuration);
            }
        }
        
        internal static async Task StopServerAsync()
        {
            if (!cancellationTokenSource.IsCancellationRequested)
            {
                cancellationTokenSource.Cancel();
            }
            
            // Check if listener exists and is active before trying to stop it
            if (listener != null && listener.Server.IsBound) 
            {
                listener.Stop();
            }
            // Give some time for tasks to acknowledge cancellation if needed.
            // Depending on AcceptClientsAsync and HandleClientAsync, further graceful shutdown logic might be added.
            await Task.Delay(100); // Small delay to allow background tasks to notice cancellation.
        }

        static async Task RunServerAsync(IConfiguration configuration)
        {
            int port = configuration.GetSection("ServerConfig").GetValue<int>("Port", 8888);
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine($"Server started on port {port}...");
            
            // AcceptClientsAsync will now run until cancellationTokenSource is cancelled
            await AcceptClientsAsync(cancellationTokenSource.Token);
        }

        private static async Task AcceptClientsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    _ = HandleClientAsync(client, cancellationToken);
                }
                catch (ObjectDisposedException)
                {
                    break; // Listener has been stopped.
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error accepting client: {ex.Message}");
                }
            }
        }

        static async Task RunClientAsync(string serverAddress, IConfiguration configuration)
        {
            TcpClient client = new TcpClient(); 
            CancellationTokenSource clientActivityTokenSource = new CancellationTokenSource();

            // Link the global token to the local one
            cancellationTokenSource.Token.Register(() => clientActivityTokenSource.Cancel());

            try
            {
                int port = configuration.GetSection("ServerConfig").GetValue<int>("Port", 8888);
                await client.ConnectAsync(serverAddress, port);
                Console.WriteLine($"Connected to server at {serverAddress}:{port}. Type 'quit' to exit.");

                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream))
                using (var writer = new StreamWriter(stream) { AutoFlush = true })
                {
                    // Task for reading console input and sending messages
                    var consoleInputTask = Task.Run(async () =>
                    {
                        while (!clientActivityTokenSource.Token.IsCancellationRequested)
                        {
                            Console.Write("Enter message: "); 
                            string message = await Console.In.ReadLineAsync(); 

                            if (string.IsNullOrEmpty(message)) continue;

                            await writer.WriteLineAsync(message);
                            if (message.ToLowerInvariant() == "quit")
                            {
                                clientActivityTokenSource.Cancel(); 
                                break;
                            }
                        }
                    }, clientActivityTokenSource.Token);

                    // Task for receiving messages from the server
                    var serverMessagesTask = Task.Run(async () =>
                    {
                        try
                        {
                            while (!clientActivityTokenSource.Token.IsCancellationRequested)
                            {
                                string messageFromServer = await reader.ReadLineAsync(clientActivityTokenSource.Token); 
                                if (messageFromServer == null) 
                                {
                                    Console.WriteLine("Server closed the connection.");
                                    clientActivityTokenSource.Cancel();
                                    break;
                                }
                                Console.WriteLine(messageFromServer);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            Console.WriteLine("Disconnected from server (operation cancelled).");
                        }
                        catch (IOException ex)
                        {
                            Console.WriteLine($"Connection lost: {ex.Message}");
                            clientActivityTokenSource.Cancel(); 
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error receiving message: {ex.Message}");
                            clientActivityTokenSource.Cancel(); 
                        }
                    }, clientActivityTokenSource.Token);

                    await Task.WhenAny(consoleInputTask, serverMessagesTask);
                }
            }
            catch (SocketException ex) 
            {
                Console.WriteLine($"Could not connect to server: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Client error: {ex.Message}");
            }
            finally
            {
                if (!clientActivityTokenSource.IsCancellationRequested)
                {
                    clientActivityTokenSource.Cancel(); 
                }
                if (client.Connected)
                {
                    client.Close(); 
                }
                Console.WriteLine("Client disconnected.");
            }
        }

        private static async Task HandleClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            string clientId = Guid.NewGuid().ToString();
            MyClient myClient = new(tcpClient, clientId);

            if (!clients.TryAdd(clientId, myClient))
            {
                Console.WriteLine($"Failed to add client {clientId}.");
                return;
            }

            Console.WriteLine($"Client {clientId} connected. Total clients: {clients.Count}");

            try
            {
                await myClient.HandleClientAsync(BroadcastMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling client {clientId}: {ex.Message}");
            }
            finally
            {
                string clientEndpointDescription = "Unknown";
                try
                {
                    // Prefer using the RemoteEndPoint for a more user-friendly identifier in messages.
                    clientEndpointDescription = tcpClient.Client.RemoteEndPoint?.ToString() ?? $"guid:{clientId}";
                }
                catch (ObjectDisposedException) // In case tcpClient was disposed elsewhere.
                {
                    clientEndpointDescription = $"guid:{clientId} (endpoint unavailable)";
                }

                if (clients.TryRemove(clientId, out MyClient removedClientContext)) 
                {
                    Console.WriteLine($"Client {clientEndpointDescription} disconnected. Total clients: {clients.Count}");
                    BroadcastMessage($"Server: Client {clientEndpointDescription} has disconnected.");
                    
                    // Ensure the client's resources are cleaned up.
                    removedClientContext.ClientCancelToken.Cancel(); // Proactively signal cancellation
                    removedClientContext.TcpClient.Close(); // Close the actual connection
                }
                // If TryRemove fails (e.g. client was never fully added or already removed), there's nothing more to do here.
            }
        }

        private static void BroadcastMessage(string message)
        {
            foreach (var (clientId, myClient) in clients)
            {
                if (!myClient.ClientCancelToken.IsCancellationRequested)
                {
                    myClient.MessageQueue.Enqueue(message);
                }
            }
        }
    }

    public class MyClient
    {
        public TcpClient TcpClient { get; }
        public CancellationTokenSource ClientCancelToken { get; } = new();
        public ConcurrentQueue<string> MessageQueue { get; } = new();

        public MyClient(TcpClient tcpClient, string id)
        {
            TcpClient = tcpClient;
        }

        public async Task HandleClientAsync(Action<string> broadcastAction, CancellationToken cancellationToken)
        {
            try
            {
                using (var stream = TcpClient.GetStream())
                using (var reader = new StreamReader(stream))
                using (var writer = new StreamWriter(stream) { AutoFlush = true })
                {
                    _ = ProcessMessagesAsync(writer, cancellationToken);

                    await writer.WriteLineAsync("Welcome to the chat server! Type 'quit' to exit.");

                    while (!ClientCancelToken.IsCancellationRequested)
                    {
                        var message = await reader.ReadLineAsync(ClientCancelToken.Token);
                        if (message?.ToLowerInvariant() == "quit")
                        {
                            await ClientCancelToken.CancelAsync();
                            break;
                        }

                        broadcastAction($"{TcpClient.Client.RemoteEndPoint}: {message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore
            }
            catch (IOException)
            {
                // Ignore
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling client: {ex.Message}");
            }
            finally
            {
                await ClientCancelToken.CancelAsync();
            }
        }

        private async Task ProcessMessagesAsync(StreamWriter writer, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && !ClientCancelToken.IsCancellationRequested)
            {
                if (MessageQueue.TryDequeue(out string message))
                {
                    try
                    {
                        await writer.WriteLineAsync(message);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error sending message to client {TcpClient.Client.RemoteEndPoint}: {ex.Message}");
                        // For now, just logging as requested. Client disconnection could be considered for future enhancements if certain errors are critical.
                    }
                }
                else
                {
                    await Task.Delay(100, ClientCancelToken.Token); // Reduce CPU usage
                }
            }
        }
    }
}
