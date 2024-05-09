using System.Collections.Generic;
using UnityEngine;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;

public class Server : MonoBehaviour
{

    public int port;
    public int bufferSize;
    public float ticksPerSecond;
    public int playerCount;
    IPEndPoint ipEndPoint;
    TcpListener listener;
    Thread serverThread;
    List<ClientInformation> clientInformationList;
    byte[] buffer;

    bool gameStarted;
    float timeSinceGameStarted;
    // Start is called before the first frame update
    async void Start()
    {
        clientInformationList = new List<ClientInformation>();
        buffer = new byte[bufferSize];
        // Setup IP endpoint
        var hostName = Dns.GetHostName();
        IPHostEntry localHost = await Dns.GetHostEntryAsync(hostName); 
        IPAddress localIpAddress = localHost.AddressList[0];
        ipEndPoint = new IPEndPoint(localIpAddress, port);
        
        // Setup TcpListener
        listener = new TcpListener(ipEndPoint);
        listener.Start();
        Debug.Log($"Server started on port: {port} and address: {localIpAddress}");
        // Start server thread
        serverThread = new Thread(ListenForClients);
        serverThread.Start();
    }

    // Update is called once per frame
    void Update()
    {
        if(gameStarted){
            timeSinceGameStarted += Time.deltaTime;
            Debug.Log(timeSinceGameStarted);
        }
        else if(clientInformationList.Count == playerCount){
            bool allUsersReady = true;
            foreach (ClientInformation clientInformation in clientInformationList){
                if(clientInformation.username == ""){
                    allUsersReady = false;
                }
            }
            gameStarted = allUsersReady;
        }
    }
    
    private void ListenForClients(){
        try{
            while(true){
            if(clientInformationList.Count == playerCount){
                Thread.CurrentThread.Abort();
            }
            if(listener != null && listener.Pending()){
                TcpClient nextClient = listener.AcceptTcpClient();
                NetworkStream nextStream = nextClient.GetStream();
                Thread nextThread = new Thread(() => ListenForClientMessage(nextClient, nextStream));
                nextThread.Start();
                clientInformationList.Add(new ClientInformation(nextClient, nextStream, nextThread, ""));
            }
            }
        }
        catch(SocketException e){
            Debug.Log(e.Message);
        }
    }
    private void ListenForClientMessage(TcpClient client, NetworkStream stream){
        try{
            while(true){
                if(client.Connected){
                    byte[] buffer;
                    while(client.ReceiveBufferSize > 0){
                        buffer = new byte[client.ReceiveBufferSize];
                        stream.Read(buffer, 0, client.ReceiveBufferSize);
                        string msg = Encoding.ASCII.GetString(buffer);
                        string[] splitMessage = msg.Split("%");
                        string messageType = splitMessage[0];
                        string json = splitMessage[1];
                        switch(messageType){
                            case "CLIENTCONNECTEDMESSAGE":{
                                Debug.Log("Client sent username");
                                // Add username to client
                                ClientConnectedMessage clientConnectedMessage = JsonUtility.FromJson<ClientConnectedMessage>(json);
                                clientInformationList.Find(info => info.client == client).username = clientConnectedMessage.username;
                                // Give client random position to start from and write to client
                                Vector3 randomPosition = new Vector3(3f, 3f, 3f);
                                ServerAcceptMessage serverAcceptMessage = new ServerAcceptMessage(randomPosition.x, randomPosition.y, randomPosition.z);
                                string messageString = "SERVERACCEPTMESSAGE%" + JsonUtility.ToJson(serverAcceptMessage);
                                byte[] bytes = Encoding.ASCII.GetBytes(messageString);
                                stream.Write(bytes, 0, bytes.Length);
                                break;
                            }
                            case "CLIENTMOVEDMESSAGE":{
                                Debug.Log("Received test message");
                                Debug.Log(json);
                                break;
                            }
                            case "TESTMESSAGE":{
                                break;
                            }
                        }
                    }
                }
                else{
                    clientInformationList.RemoveAll(info => info.client == client);
                    Thread.CurrentThread.Abort();
                    Debug.Log("Client disconnected. Aborting client thread and removing him.");
                }
            }
        }
        catch(SocketException e){
            Debug.Log("Exception while listening on client");
        }
    }
}