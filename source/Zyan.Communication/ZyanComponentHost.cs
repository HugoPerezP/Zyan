﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using Zyan.Communication.Security;
using Zyan.Communication.Protocols;
using Zyan.Communication.Protocols.Tcp;
using Zyan.Communication.SessionMgmt;
using Zyan.Communication.Notification;

namespace Zyan.Communication
{    
    /// <summary>
    /// Host for publishing components with Zyan.
    /// </summary>
    public class ZyanComponentHost : IDisposable
    {
        #region Constructors

        /// <summary>
        /// Konstruktor.
        /// </summary>
        /// <param name="name">Name des Komponentenhosts</param>        
        /// <param name="tcpPort">TCP-Anschlussnummer</param>                
        public ZyanComponentHost(string name, int tcpPort)
            : this(name, new TcpBinaryServerProtocolSetup(tcpPort), new InProcSessionManager(), new ComponentCatalog())
        { }

        /// <summary>
        /// Konstruktor.
        /// </summary>
        /// <param name="name">Name des Komponentenhosts</param>        
        /// <param name="tcpPort">TCP-Anschlussnummer</param>                
        /// <param name="catalog">Komponenten-Katalog</param>
        public ZyanComponentHost(string name, int tcpPort, ComponentCatalog catalog)
            : this(name, new TcpBinaryServerProtocolSetup(tcpPort), new InProcSessionManager(), catalog)
        { }

        /// <summary>
        /// Konstruktor.
        /// </summary>
        /// <param name="name">Name des Komponentenhosts</param>        
        /// <param name="protocolSetup">Protokoll-Einstellungen</param>        
        public ZyanComponentHost(string name, IServerProtocolSetup protocolSetup)
            : this(name, protocolSetup, new InProcSessionManager(), new ComponentCatalog())
        { }

        /// <summary>
        /// Konstruktor.
        /// </summary>
        /// <param name="name">Name des Komponentenhosts</param>        
        /// <param name="protocolSetup">Protokoll-Einstellungen</param>        
        /// <param name="catalog">Komponenten-Katalog</param>
        public ZyanComponentHost(string name, IServerProtocolSetup protocolSetup, ComponentCatalog catalog)
            : this(name, protocolSetup, new InProcSessionManager(), catalog)
        { }

        /// <summary>
        /// Konstruktor.
        /// </summary>
        /// <param name="name">Name des Komponentenhosts</param>        
        /// <param name="protocolSetup">Protokoll-Einstellungen</param>
        /// <param name="sessionManager">Sitzungsverwaltung</param>
        public ZyanComponentHost(string name, IServerProtocolSetup protocolSetup, ISessionManager sessionManager) : this(name,protocolSetup,sessionManager,new ComponentCatalog())
        { }

        /// <summary>
        /// Konstruktor.
        /// </summary>
        /// <param name="name">Name des Komponentenhosts</param>        
        /// <param name="protocolSetup">Protokoll-Einstellungen</param>        
        /// <param name="sessionManager">Sitzungsverwaltung</param>
        /// <param name="catalog">Komponenten-Katalog</param>
        public ZyanComponentHost(string name, IServerProtocolSetup protocolSetup, ISessionManager sessionManager, ComponentCatalog catalog)
        {
            // Wenn kein Name angegeben wurde ...
            if (string.IsNullOrEmpty(name))
                // Ausnahme werfen
                throw new ArgumentException(LanguageResource.ArgumentException_ComponentHostNameMissing,"name");

            // Wenn keine Protokoll-Einstellungen angegeben wurde ...
            if (protocolSetup == null)
                // Ausnahme werfen
                throw new ArgumentNullException("protocolSetup");

            // Wenn keine Sitzungsverwaltung übergeben wurde ...
            if (sessionManager == null)
                // Ausnahme werfen
                throw new ArgumentNullException("sessionManager");

            // Wenn kein Komponenten-Katalog angegeben wurde ...
            if (catalog == null)
                // Ausnahme werfen
                throw new ArgumentNullException("catalog");

            // Werte übernehmen
            _name = name;
            _protocolSetup = protocolSetup;
            _sessionManager = sessionManager;
            _catalog = catalog;
            
            // Verwaltung für Serialisierungshandling erzeugen
            _serializationHandling = new SerializationHandlerRepository();
            
            // Komponentenaufrufer erzeugen
            _invoker = new ZyanDispatcher(this);

            // Authentifizierungsanbieter übernehmen und verdrahten
            _authProvider = protocolSetup.AuthenticationProvider;
            this.Authenticate = _authProvider.Authenticate;

            // Komponenten Host der Host-Auflistung zufügen
            _hosts.Add(this);

            // Beginnen auf Client-Anfragen zu horchen
            StartListening();
        }

        /// <summary>
        /// Destruktor.
        /// </summary>
        ~ZyanComponentHost()
        {
            // Ressourcen freigeben
            Dispose();
        }

        #endregion
        
        #region Authentication

        private IAuthenticationProvider _authProvider = null;

        /// <summary>
        /// Request for authentication.
        /// </summary>
        public Func<AuthRequestMessage, AuthResponseMessage> Authenticate;     

        #endregion

        #region Session Management

        private ISessionManager _sessionManager = null;
        
        /// <summary>
        /// Returns the session manager used by this host.
        /// </summary>
        public ISessionManager SessionManager
        {
            get { return _sessionManager; }            
        }

        #endregion

        #region Component Hosting

        private ComponentCatalog _catalog = null;
        private ZyanDispatcher _invoker = null;
        private static List<ZyanComponentHost> _hosts = new List<ZyanComponentHost>();

        /// <summary>
        /// Gibt eine Auflistung aller bekanten Komponenten Hosts zurück.
        /// </summary>
        public static List<ZyanComponentHost> Hosts
        {
            get { return _hosts.ToList<ZyanComponentHost>(); }
        }

        /// <summary>
        /// Gibt den zu veröffentlichenden Komponenten-Katalog zurück, oder legt ihn fest.
        /// </summary>
        public ComponentCatalog ComponentCatalog
        {
            get { return _catalog; }
            set
            { 
                // Wenn kein Wert angegeben wurde ...
                if (value == null)
                    // Ausnahme werfen
                    throw new ArgumentNullException();

                // Komponenten-Katalog festlegen
                _catalog = value;
            }
        }

        /// <summary>
        /// Gibt die Liste der Registrierten Komponenten zurück.
        /// <remarks>Falls die Liste noch nicht existiert, wird sie automatisch erstellt.</remarks>
        /// </summary>
        internal Dictionary<string, ComponentRegistration> ComponentRegistry
        { 
            get
            {        
                // Liste zurückgeben
                return _catalog.ComponentRegistry;
            }
        }

        /// <summary>
        /// Gibt eine Instanz einer registrierten Komponente zurück.
        /// </summary>
        /// <param name="registration">Komponentenregistrierung</param>
        /// <returns>Komponenten-Instanz</returns>
        internal object GetComponentInstance(ComponentRegistration registration)
        {
            // Aufruf an Komponentenkatalog weiterleiten
            return _catalog.GetComponentInstance(registration);
        }

        /// <summary>
        /// Hebt die Registrierung einer bestimmten Komponente auf.
        /// </summary>
        /// <typeparam name="I">Schnittstellentyp der Komponente</typeparam>
        public void UnregisterComponent<I>()
        {
            // Aufruf an Komponentenkatalog weiterleiten
            _catalog.UnregisterComponent<I>();            
        }

        /// <summary>
        /// Hebt die Registrierung einer bestimmten Komponente auf.
        /// </summary>
        /// <param name="uniqueName">Eindeutiger Name</param>
        public void UnregisterComponent(string uniqueName)
        {
            // Aufruf an Komponentenkatalog weiterleiten
            _catalog.UnregisterComponent(uniqueName);
        }

        /// <summary>
        /// Registriert eine bestimmte Komponente.
        /// </summary>        
        /// <typeparam name="I">Schnittstellentyp der Komponente</typeparam>
        /// <typeparam name="T">Implementierungstyp der Komponente</typeparam>
        public void RegisterComponent<I, T>()
        {
            // Aufruf an Komponentenkatalog weiterleiten
            _catalog.RegisterComponent<I, T>();            
        }

        /// <summary>
        /// Registriert eine bestimmte Komponente.
        /// </summary>        
        /// <param name="activationType">Aktivierungsart</param>
        /// <typeparam name="I">Schnittstellentyp der Komponente</typeparam>
        /// <typeparam name="T">Implementierungstyp der Komponente</typeparam>
        public void RegisterComponent<I, T>(ActivationType activationType)
        {
            // Aufruf an Komponentenkatalog weiterleiten
            _catalog.RegisterComponent<I, T>(activationType);
        }
        
        /// <summary>
        /// Registriert eine bestimmte Komponente.
        /// </summary>
        /// <typeparam name="I">Schnittstellentyp der Komponente</typeparam>
        /// <param name="factoryMethod">Delegat auf Fabrikmethode, die sich um die Erzeugung und Inizialisierung der Komponente kümmert</param>
        public void RegisterComponent<I>(Func<object> factoryMethod)
        {
            // Aufruf an Komponentenkatalog weiterleiten
            _catalog.RegisterComponent<I>(factoryMethod);            
        }

        /// <summary>
        /// Registriert eine bestimmte Komponente.
        /// </summary>
        /// <typeparam name="I">Schnittstellentyp der Komponente</typeparam>
        /// <param name="factoryMethod">Delegat auf Fabrikmethode, die sich um die Erzeugung und Inizialisierung der Komponente kümmert</param>
        /// <param name="activationType">Aktivierungstyp</param>
        public void RegisterComponent<I>(Func<object> factoryMethod, ActivationType activationType)
        {
            // Aufruf an Komponentenkatalog weiterleiten
            _catalog.RegisterComponent<I>(factoryMethod,activationType);
        }

        /// <summary>
        /// Registriert eine bestimmte Komponenteninstanz.
        /// </summary>
        /// <typeparam name="I">Schnittstellentyp der Komponente</typeparam>
        /// <typeparam name="T">Implementierungstyp der Komponente</typeparam>
        /// <param name="instance">Instanz</param>
        public void RegisterComponent<I, T>(T instance)
        {
            // Aufruf an Komponentenkatalog weiterleiten
            _catalog.RegisterComponent<I, T>(instance);            
        }

        /// <summary>
        /// Registriert eine bestimmte Komponente.
        /// </summary>        
        /// <typeparam name="I">Schnittstellentyp der Komponente</typeparam>
        /// <typeparam name="T">Implementierungstyp der Komponente</typeparam>
        /// <param name="uniqueName">Eindeutiger Name</param>
        public void RegisterComponent<I, T>(string uniqueName)
        {
            // Aufruf an Komponentenkatalog weiterleiten
            _catalog.RegisterComponent<I, T>(uniqueName);            
        }

        /// <summary>
        /// Registriert eine bestimmte Komponente.
        /// </summary>        
        /// <typeparam name="I">Schnittstellentyp der Komponente</typeparam>
        /// <typeparam name="T">Implementierungstyp der Komponente</typeparam>
        /// <param name="uniqueName">Eindeutiger Name</param>
        /// <param name="activationType">Aktivierungstyp</param>
        public void RegisterComponent<I, T>(string uniqueName, ActivationType activationType)
        {
            // Aufruf an Komponentenkatalog weiterleiten
            _catalog.RegisterComponent<I, T>(uniqueName, activationType);
        }

        /// <summary>
        /// Registriert eine bestimmte Komponente.
        /// </summary>
        /// <typeparam name="I">Schnittstellentyp der Komponente</typeparam>
        /// <param name="uniqueName">Eindeutiger Name</param>
        /// <param name="factoryMethod">Delegat auf Fabrikmethode, die sich um die Erzeugung und Inizialisierung der Komponente kümmert</param>
        public void RegisterComponent<I>(string uniqueName, Func<object> factoryMethod)
        {
            // Aufruf an Komponentenkatalog weiterleiten
            _catalog.RegisterComponent<I>(uniqueName, factoryMethod);            
        }

        /// <summary>
        /// Registriert eine bestimmte Komponente.
        /// </summary>
        /// <typeparam name="I">Schnittstellentyp der Komponente</typeparam>
        /// <param name="uniqueName">Eindeutiger Name</param>
        /// <param name="factoryMethod">Delegat auf Fabrikmethode, die sich um die Erzeugung und Inizialisierung der Komponente kümmert</param>
        /// <param name="activationType">Aktivierungstyp</param>
        public void RegisterComponent<I>(string uniqueName, Func<object> factoryMethod, ActivationType activationType)
        {
            // Aufruf an Komponentenkatalog weiterleiten
            _catalog.RegisterComponent<I>(uniqueName, factoryMethod, activationType);
        }

        /// <summary>
        /// Registriert eine bestimmte Komponenteninstanz.
        /// </summary>
        /// <typeparam name="I">Schnittstellentyp der Komponente</typeparam>
        /// <typeparam name="T">Implementierungstyp der Komponente</typeparam>
        /// <param name="uniqueName">Eindeutiger Name</param>
        /// <param name="instance">Instanz</param>        
        public void RegisterComponent<I, T>(string uniqueName, T instance)
        {
            // Aufruf an Komponentenkatalog weiterleiten
            _catalog.RegisterComponent<I, T>(uniqueName, instance);            
        }

        /// <summary>
        /// Gibt eine Liste mit allen registrierten Komponenten zurück.
        /// </summary>
        /// <returns>Liste der registrierten Komponenten</returns>
        public List<ComponentInfo> GetRegisteredComponents()
        { 
            // Aufruf an Komponentenkatalog weiterleiten
            return _catalog.GetRegisteredComponents();
        }

        #endregion               

        #region Network Communication

        private IServerProtocolSetup _protocolSetup = null;

        // Name dieses Komponentenhosts
        private string _name = string.Empty;

        // Kanalname
        private string _channelName = string.Empty;

        /// <summary>
        /// Gibt den Namen des Komponentenhosts zurück.
        /// </summary>
        public string Name
        {
            get { return _name; }
        }

        /// <summary>
        /// Startet das Horchen auf Client-Anfragen.
        /// </summary>
        private void StartListening()
        {
            // TCP-Kommunikationskanal öffnen
            IChannel channel = _protocolSetup.CreateChannel();
                        
            // Wenn der Kanal erzeugt wurde ...
            if (channel != null)
            {
                // Kanalnamen merken
                _channelName = channel.ChannelName;

                // Kanal registrieren
                ChannelServices.RegisterChannel(channel, false);

                // Komponentenhost für entfernte Zugriffe veröffentlichen            
                RemotingServices.Marshal(_invoker, _name);
            }
            else
                throw new ApplicationException(LanguageResource.ApplicationException_NoChannel);
        }

        /// <summary>
        /// Beendet das Horchen auf Client-Anfragen.
        /// </summary>
        private void StopListening()
        {
            // Veröffentlichung des Komponentenhosts für entfernte Zugriffe löschen
            RemotingServices.Disconnect(_invoker);

            // Kommunikationskanal schließen
            CloseChannel();                                        
        }

        /// <summary>
        /// Schließt den Kanal, falls dieser geöffent ist.
        /// </summary>
        private void CloseChannel()
        { 
            // Kanal suchen
            IChannel channel = ChannelServices.GetChannel(_channelName);

            // Wenn der Kanal gefunden wurde ...
            if (channel != null)             
                // Kanalregistrierung aufheben
                ChannelServices.UnregisterChannel(channel);            
        }

        #endregion

        #region Policy Injection

        /// <summary>
        /// Ereignis: Bevor ein Komponentenaufruf durchgeführt wird.
        /// </summary>
        public event EventHandler<BeforeInvokeEventArgs> BeforeInvoke;

        /// <summary>
        /// Ereignis: Nachdem ein Komponentenaufruf durchgeführt wurde.
        /// </summary>
        public event EventHandler<AfterInvokeEventArgs> AfterInvoke;

        /// <summary>
        /// Ereignis: Wenn ein Komponentenaufruf abgebrochen wurde.
        /// </summary>
        public event EventHandler<InvokeCanceledEventArgs> InvokeCanceled;

        /// <summary>
        /// Gibt zurück, ob für das BeforeInvoke-Ereignis Abos registriert wurden.
        /// </summary>
        /// <returns>Wahr, wenn Abos vorhanden, ansonsten Falsch</returns>
        protected internal bool HasBeforeInvokeSubscriptions()
        { 
            return (BeforeInvoke != null);
        }

        /// <summary>
        /// Gibt zurück, ob für das AfterInvoke-Ereignis Abos registriert wurden.
        /// </summary>
        /// <returns>Wahr, wenn Abos vorhanden, ansonsten Falsch</returns>
        protected internal bool HasAfterInvokeSubscriptions()
        {
            return (AfterInvoke != null);
        }

        /// <summary>
        /// Gibt zurück, ob für das InvokeCanceled-Ereignis Abos registriert wurden.
        /// </summary>
        /// <returns>Wahr, wenn Abos vorhanden, ansonsten Falsch</returns>
        protected internal bool HasInvokeCanceledSubscriptions()
        {
            return (InvokeCanceled != null);
        }

        /// <summary>
        /// Feuert das BeforeInvoke-Ereignis.
        /// </summary>
        /// <param name="e">Ereignisargumente</param>
        protected internal virtual void OnBeforeInvoke(BeforeInvokeEventArgs e)
        {
            // Wenn für BeforeInvoke Ereignisprozeduren registriert sind ...
            if (BeforeInvoke != null)
                // Ereignis feuern
                BeforeInvoke(this, e);
        }

        /// <summary>
        /// Feuert das AfterInvoke-Ereignis.
        /// </summary>
        /// <param name="e">Ereignisargumente</param>
        protected internal virtual void OnAfterInvoke(AfterInvokeEventArgs e)
        {
            // Wenn für AfterInvoke Ereignisprozeduren registriert sind ...
            if (AfterInvoke != null)
                // Ereignis feuern
                AfterInvoke(this, e);
        }

        /// <summary>
        /// Feuert das InvokeCanceled-Ereignis.
        /// </summary>
        /// <param name="e">Ereignisargumente</param>
        protected internal virtual void OnInvokeCanceled(InvokeCanceledEventArgs e)
        {
            // Wenn für AfterInvoke Ereignisprozeduren registriert sind ...
            if (InvokeCanceled != null)
                // Ereignis feuern
                InvokeCanceled(this, e);
        }

        #endregion

        #region Notifications

        // Benachrichtigungsdienst
        private volatile NotificationService _notificationService = null;
                
        // Sperrobjekt für Instanzerstellung des Benachrichtigungsdienstes 
        private object _notificationServiceLockObject = new object();

        /// <summary>
        /// Gibt zurück, ob der Benachrichtigungsdienst läuft, oder nicht.
        /// </summary>
        public bool IsNotificationServiceRunning
        {
            get
            {
                lock (_notificationServiceLockObject)
                {
                    return _notificationService != null;
                }
            }
        }

        /// <summary>
        /// Startet den Benachrichtigungsdienst.
        /// </summary>
        public void StartNotificationService()
        {
            lock (_notificationServiceLockObject)
            { 
                // Wenn der Dienst nicht bereits läuft ...
                if (_notificationService == null)
                { 
                    // Instanz erzeugen
                    _notificationService = new NotificationService();
                }
            }
        }
        
        /// <summary>
        /// Beendet den Benachrichtigungsdienst.
        /// </summary>
        public void StopNotificationService()
        {
            lock (_notificationServiceLockObject)
            {
                // Wenn der Dienst läuft ...
                if (_notificationService != null)
                {
                    // Instanz löschen
                    _notificationService = null;
                }
            }
        }

        /// <summary>
        /// Gibt den Benachrichtigungsdienst zurück.
        /// </summary>
        public NotificationService NotificationService
        {
            get
            {
                lock (_notificationServiceLockObject)
                {
                    return _notificationService;
                }
            }
        }

        /// <summary>
        /// Veröffentlicht ein Ereignis einer Serverkomponente.
        /// </summary>
        /// <param name="eventName">Ereignisname</param>
        /// <returns>Delegat für Benachrichtigungsversand an registrierte Clients</returns>
        public EventHandler<NotificationEventArgs> PublishEvent(string eventName)
        {
            // Wenn kein Benachrichtigungsdienst läuft ...
            if (!IsNotificationServiceRunning)
                // Ausnahme werfen
                throw new ApplicationException(LanguageResource.ApplicationException_NotificationServiceNotRunning);

            // Sendevorrichtung erstellen
            NotificationSender sender = new NotificationSender(NotificationService, eventName);

            // Delegat auf Methode zum Benachrichtigungsversand erzeugen
            EventHandler<NotificationEventArgs> sendHandler = new EventHandler<NotificationEventArgs>(sender.HandleServerEvent);

            // Delegat zurückgeben
            return sendHandler;
        }

        #endregion

        #region User defined Serialization Handling

        // Serialisierungshandling.
        private SerializationHandlerRepository _serializationHandling = null;

        /// <summary>
        /// Gibt die Verwaltung für benutzerdefinierte Serialisierungsbehandlung zurück.
        /// </summary>
        public SerializationHandlerRepository SerializationHandling
        {
            get { return _serializationHandling; }
        }

        #endregion

        #region IDisposable Implementation

        // Gibt an, ob Dispose bereits aufgerufen wurde, oder nicht
        private bool _isDisposed = false;

        /// <summary>
        /// Verwaltete Ressourcen freigeben.
        /// </summary>
        public void Dispose()
        {
            // Wenn Dispose noch nicht aufgerufen wurde ...
            if (!_isDisposed)
            {
                // Schalter setzen
                _isDisposed = true;

                // Host aus der Auflistung entfernen
                _hosts.Remove(this);

                // Horchen auf Client-Anfragen beenden
                StopListening();
                
                // Wenn der Komponentenaufrufer existiert ...
                if (_invoker != null)
                    // Komponnetenaufrufer entsorgen
                    _invoker = null;
                
                // Wenn die Sitzungsverwaltung existiert ...
                if (_sessionManager != null)
                {
                    // Sitzungsverwaltung entsorgen
                    _sessionManager.Dispose();
                    _sessionManager = null;
                }
                // Wenn die Authentifizierung verdrahtet ist ...
                if (this.Authenticate != null)
                    // Verdrahtung aufheben
                    this.Authenticate = null;

                // Wenn der Authentifizierungsanbieter existiert ...
                if (_authProvider != null)
                    // Authentifizierungsanbieter entsorgen
                    _authProvider = null;

                // Wenn der Komponentenkatalog existiert ...
                if (_catalog != null)
                {
                    // Komponentenkatalog entsorgen                    
                    _catalog = null;
                }
            }
        }
        
        #endregion

        #region Login Events

        public event EventHandler<LoginEventArgs> ClientLoggedOn;
        public event EventHandler<LoginEventArgs> ClientLoggedOff;

        /// <summary>
        /// Fires "ClientLoggedOn" event.
        /// </summary>
        /// <param name="e">Arguments</param>
        protected internal void OnClientLoggedOn(LoginEventArgs e)
        {
            if (ClientLoggedOn!=null)
                ClientLoggedOn(this,e);
        }

        /// <summary>
        /// Fires "ClientLoggedOff" event.
        /// </summary>
        /// <param name="e">Arguments</param>
        protected internal void OnClientLoggedOff(LoginEventArgs e)
        {
            if (ClientLoggedOff != null)
                ClientLoggedOff(this, e);
        }

        #endregion
    }
}
