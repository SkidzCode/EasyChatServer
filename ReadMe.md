# SimpleChatServer

Welcome to **SimpleChatServer**! This is the simplest thread-safe chat server implemented in C# using .NET 8. The project aims to provide a foundational understanding of client-server communication while maintaining thread safety.

## Features

- **Thread-safe**: The server is designed to handle multiple clients concurrently without data inconsistency.
- **Minimal Setup**: Lightweight design makes it easy to understand and extend.
- **TCP-based Communication**: Utilizes TCP for reliable communication between clients and the server.

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installed on your machine.
- Familiarity with basic C# programming and command-line operations.

### Installation

1. Clone the repository:

   ```bash
   git clone https://github.com/yourusername/SimpleChatServer.git
   cd SimpleChatServer
   ```

2. Build the project:

   ```bash
   dotnet build
   ```

3. Run the server:

   ```bash
   dotnet run
   ```

   You can also run as a client by providing the server address:

   ```bash
   dotnet run [SERVER_IP_ADDRESS]
   ```

### Usage

- Connect multiple clients to the server by running instances of the client with the server address.
- Type messages into the client console to chat with other connected clients.
- Type 'quit' to disconnect from the server.

## Code Overview

The server creates a TcpListener on port 8888 and can accept multiple clients concurrently. Each client runs in its own thread, allowing for real-time messaging. The `ConcurrentDictionary` ensures thread safety when managing connected clients.

### Main Components

- **Program.cs**: The main entry point that orchestrates the server and client logic.
- **MyClient Class**: Manages individual client connections and message processing.
- **Broadcasting**: Messages sent by one client are broadcasted to all connected clients.

## License

This project is licensed under the Mozilla Public License 2.0. However, **commercial users are required to contact the author to obtain a separate licensing agreement**. 

## Acknowledgments

- This project was inspired by the need for a simple communication tool and for learning purposes.
  
## Contact

For more information, please feel free to reach out to me.
```

### Notes:
- Ensure to replace `yourusername` with your actual GitHub username and add your email at the end for contact information.
- You may want to provide additional details or links if you have specific contributions, references, or acknowledgments to mention.