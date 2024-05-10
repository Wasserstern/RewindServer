using System.Collections.Generic;
using UnityEngine;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;

public class Server : MonoBehaviour
{

    [Header("Server Settings")]
    public int port;
    public int bufferSize;
    public float ticksPerSecond;
    public int playerCount;
    public float maxMoveDistancePerTick;
    

    // Runtime variables
    IPEndPoint ipEndPoint;
    TcpListener listener;
    Thread serverThread;
    List<ClientInformation> clientInformationList;
    ConcurrentQueue<ClientMovedMessage> clientMovedMessageQueue;
    Queue<WorldState> previousWorldStates;
    byte[] buffer;

    bool gameStarted;
    float timeSinceGameStarted;
    [SerializeField]
    int currentTick;
    float tickTimer;
    // Start is called before the first frame update
    async void Start()
    {
        clientInformationList = new List<ClientInformation>();
        clientMovedMessageQueue = new ConcurrentQueue<ClientMovedMessage>();
        previousWorldStates = new Queue<WorldState>();
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
            tickTimer += Time.deltaTime;
            Debug.Log(timeSinceGameStarted);

            if(tickTimer >= 1f / ticksPerSecond){
                // Save current world state
                previousWorldStates.Enqueue( new WorldState(clientInformationList, timeSinceGameStarted));
                if(previousWorldStates.Count > 120){
                    previousWorldStates.Dequeue();
                }
                // Start tick logic
                Tick();
                currentTick++;
                tickTimer = 0f;
            }

        }
        else if(clientInformationList.Count == playerCount){
            bool allUsersReady = true;
            foreach (ClientInformation clientInformation in clientInformationList){
                if(clientInformation.username == ""){
                    allUsersReady = false;
                }
            }
            gameStarted = allUsersReady;
            StartGame();
        }
    }
    /// <summary>
    /// Handles all tick logic. 
    /// Checks all unhandled messages. Sends server simulation information to clients.
    /// </summary>
    private void Tick(){
        lock(clientMovedMessageQueue){
            while(clientMovedMessageQueue.Count > 0){
                ClientMovedMessage moveMsg;
                clientMovedMessageQueue.TryDequeue(out moveMsg);
                if(moveMsg != null){
                    // Check if new position is valid.
                    Vector3 newPosition = new Vector3(moveMsg.x, moveMsg.y, moveMsg.z);
                    ClientInformation messageClient = clientInformationList.Find(info => info.username == moveMsg.username);
                    if(Vector3.Distance(messageClient.currentPosition, newPosition) > maxMoveDistancePerTick){
                        Vector3 moveDirection = (newPosition - messageClient.currentPosition).normalized;
                        newPosition = messageClient.currentPosition + moveDirection * maxMoveDistancePerTick;
                    }

                    messageClient.currentPosition = newPosition;
                }
                else{
                    throw new System.Exception("Dequeue of clientMovedMessage failed!");
                }
            }
            ServerWorldStateMessage serverWorldStateMessage = new ServerWorldStateMessage(new WorldState(clientInformationList, timeSinceGameStarted), currentTick);
            string msg = "SERVERWORLDSTATEMESSAGE%" + JsonUtility.ToJson(serverWorldStateMessage);
            byte[] bytes = Encoding.UTF8.GetBytes(msg);
            foreach(ClientInformation info in clientInformationList){
                info.stream.Write(bytes, 0, bytes.Length);
            }
        }
    }
    /// <summary>
    /// Sends a start game message to each player. Tick counting starts or resets here.
    /// Starts server side simulation.
    /// </summary>
    private void StartGame(){
        foreach(ClientInformation info in clientInformationList)
        {
            try{
                ServerGameStartedMessage serverGameStartedMessage = new ServerGameStartedMessage();
                string msg = "SERVERGAMESTARTEDMESSAGE%" + JsonUtility.ToJson(serverGameStartedMessage);
                byte[] bytes = Encoding.UTF8.GetBytes(msg);
                // TODO: Check if this blocking call fucks up Unitys main thread completely or just blocks for a short time
                info.stream.Write(bytes, 0, bytes.Length);
            }
            catch(SocketException e){
                Debug.Log("Error when sending start mesage to client: " + e.Message);
            }

        }
    }
    
    /// <summary>
    ///  Listen for clients. This runs on a seperate thread.
    /// </summary>
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
    /// <summary>
    /// Listens for messages from each client. 
    /// This method runs on many threads, one for each accepted client connection.
    /// </summary>
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
                                ClientInformation info = clientInformationList.Find(info => info.client == client);
                                info.username = clientConnectedMessage.username;
                    
                                // Give client random position to start from and write to client
                                Vector3 randomPosition = new Vector3(3f, 3f, 3f);
                                info.currentPosition = randomPosition;
                                ServerAcceptMessage serverAcceptMessage = new ServerAcceptMessage(randomPosition.x, randomPosition.y, randomPosition.z);
                                string messageString = "SERVERACCEPTMESSAGE%" + JsonUtility.ToJson(serverAcceptMessage);
                                byte[] bytes = Encoding.ASCII.GetBytes(messageString);
                                stream.Write(bytes, 0, bytes.Length);
                                break;
                            }
                            case "CLIENTMOVEDMESSAGE":{
                                // Add move message to queue and handle later
                                ClientMovedMessage clientMovedMesssage = JsonUtility.FromJson<ClientMovedMessage>(json);
                                clientMovedMessageQueue.Enqueue(clientMovedMesssage);
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