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
        private static TcpListener listener;
        private static readonly CancellationTokenSource cancellationTokenSource = new();

        static async Task Main(string[] args)
        {
            if (args.Length == 0)
            {
                // Run as server
                await RunServerAsync();
            }
            else
            {
                // Run as client
                await RunClientAsync(args[0]);
            }
        }

        static async Task RunServerAsync()
        {
            listener = new TcpListener(IPAddress.Any, 8888);
            listener.Start();
            Console.WriteLine("Server started on port 8888...");

            Task acceptClientsTask = AcceptClientsAsync(cancellationTokenSource.Token);

            Console.WriteLine("Press ENTER to stop the server...");
            Console.ReadLine();

            cancellationTokenSource.Cancel();
            await acceptClientsTask;
            listener.Stop();
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

        static async Task RunClientAsync(string serverAddress)
        {
            try
            {
                TcpClient client = new TcpClient();
                await client.ConnectAsync(serverAddress, 8888);

                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream))
                using (var writer = new StreamWriter(stream) { AutoFlush = true })
                {
                    _ = Task.Run(async () =>
                    {
                        while (!cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            Console.Write("Enter message: ");
                            string message = Console.ReadLine();
                            await writer.WriteLineAsync(message);
                        }
                    });

                    while (!cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        string message = await reader.ReadLineAsync();
                        Console.WriteLine(message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error connecting to server: {ex.Message}");
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
                if (clients.TryRemove(clientId, out _))
                {
                    tcpClient.Close();
                    Console.WriteLine($"Client {clientId} disconnected. Total clients: {clients.Count}");
                }
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
                    catch
                    {

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
