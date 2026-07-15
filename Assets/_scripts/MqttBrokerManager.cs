using System;
using System.Threading.Tasks;
using UnityEngine;
using MQTTnet;
using MQTTnet.Server;
using System.Net;
using System.Net.Sockets;
// Prérequis :
// 1. Récupérer les DLLs MQTTnet (via NuGet -> extraire le .nupkg, ou NuGetForUnity)
//    et les placer dans Assets/Plugins/MQTTnet/
// 2. Vérifier la compatibilité .NET Standard 2.1 dans Player Settings > Api Compatibility Level
// 3. Ajouter un link.xml à la racine d'Assets pour éviter le stripping IL2CPP :
//    <linker>
//      <assembly fullname="MQTTnet" preserve="all"/>
//    </linker>

public class MqttBrokerManager : MonoBehaviour
{
    public string _currentIpAddress;

    [Header("Configuration du broker")]
    [SerializeField] private int port = 1883;

    private static MqttBrokerManager _instance;
    private MqttServer _mqttServer;
    public bool print = false;

    private void Awake()
    {
        // Protection singleton : si une instance existe déjà (doublon de
        // GameObject, scène additive, DontDestroyOnLoad...), on détruit
        // celle-ci plutôt que de tenter de démarrer un second broker sur
        // le même port.
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning("[MQTT] Une instance de MqttBrokerManager existe déjà, destruction du doublon.");
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    private void Start()
    {
        _RefreshIpAddress();
        Debug.Log($"Adresse IP : {_currentIpAddress}");
        // Démarre le broker une fois l'IP connue
        _ = StartBrokerAsync();
    }

    public void _RefreshIpAddress()
    {
        _currentIpAddress = GetLocalIpAddress();
    }

    private string GetLocalIpAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    return ip.ToString();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Réseau] Erreur lors de la récupération de l'IP : {e.Message}");
        }
        return null;
    }

    public async Task StartBrokerAsync()
    {
        if (_mqttServer != null)
        {
            Debug.LogWarning("[MQTT] StartBrokerAsync() appelé alors que le broker tourne déjà, appel ignoré.");
            return;
        }

        try
        {
            var mqttFactory = new MqttFactory();

            var options = new MqttServerOptionsBuilder()
                .WithDefaultEndpoint()
                .WithDefaultEndpointPort(port)
                .Build();

            _mqttServer = mqttFactory.CreateMqttServer(options);

            _mqttServer.ValidatingConnectionAsync += HandleClientConnection;
            _mqttServer.ClientConnectedAsync += HandleClientConnected;
            _mqttServer.InterceptingPublishAsync += HandleMessagePublished;

            await _mqttServer.StartAsync();

            if (print) Debug.Log($"[MQTT] Broker démarré sur le port {port}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[MQTT] Erreur au démarrage du broker : {e.Message}");
        }
    }

    /// <summary>
    /// valider un login/mdp si besoin
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    private Task HandleClientConnection(ValidatingConnectionEventArgs args)
    {
        // if (args.UserName != "monuser") args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
        return Task.CompletedTask;
    }

    private Task HandleClientConnected(ClientConnectedEventArgs args)
    {
        if (print) Debug.Log($"[MQTT] Client connecté : {args.ClientId}");
        return Task.CompletedTask;
    }

    private Task HandleMessagePublished(InterceptingPublishEventArgs args)
    {
        var topic = args.ApplicationMessage.Topic;
        if (print) Debug.Log($"[MQTT] Message reçu sur le topic '{topic}'");
        return Task.CompletedTask;
    }

    private void OnApplicationQuit()
    {
        StopBrokerSync();
    }

    private void OnDisable()
    {
        StopBrokerSync();
    }

    // Arrêt bloquant : garantit que le socket est bien libéré avant que
    // l'Editor ne coupe le domaine applicatif (contrairement à un simple
    // "await" dans un async void, qui peut être interrompu avant la fin).
    private void StopBrokerSync()
    {
        if (_mqttServer == null) return;

        try
        {
            _mqttServer.StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MQTT] Erreur à l'arrêt du broker : {e.Message}");
        }
        finally
        {
            _mqttServer = null;
        }
    }
}