using System.Net.Sockets;
using System.Threading;

public class ClientInformation{
    public string username;
    public TcpClient client;
    public NetworkStream stream;
    public Thread thread;
    public ClientInformation(TcpClient client, NetworkStream stream, Thread thread, string username){
        this.client = client;
        this.stream = stream;
        this.thread = thread;
        this.username = username;
        
    }
}