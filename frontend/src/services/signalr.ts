import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Message, ConversationStatus, AISuggestion } from '../types/chat';

const WS_URL = process.env.NEXT_PUBLIC_WS_URL || 'http://localhost/hubs';

export class SignalRService {
  private connection: HubConnection | null = null;
  private projectId: string;
  private token: string;
  
  // Callback registers
  private onMessageCallback?: (message: Message) => void;
  private onStatusCallback?: (convId: string, status: ConversationStatus) => void;
  private onAISuggestionCallback?: (suggestion: AISuggestion) => void;
  private onNotificationCallback?: (title: string, body: string, type: string) => void;
  private onPresenceCallback?: (agentId: string, status: 'Online' | 'Offline') => void;
  private onAITypingCallback?: (convId: string, isTyping: boolean) => void;
  private onCustomerCallback?: (customer: any) => void;

  constructor(projectId: string, token: string) {
    this.projectId = projectId;
    this.token = token;
  }

  public async start(): Promise<void> {
    if (this.connection && this.connection.state !== HubConnectionState.Disconnected) return;
    if (this.connection?.state === HubConnectionState.Disconnected) {
      this.connection = null;
    }

    const hubUrl = `${WS_URL}/notifications?projectId=${this.projectId}`;

    this.connection = new HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => this.token,
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Information)
      .build();

    // Register event listeners from server
    this.connection.on('ReceiveMessage', (message: Message) => {
      if (this.onMessageCallback) {
        this.onMessageCallback(message);
      }
    });

    this.connection.on('ConversationStatusChanged', (convId: string, status: ConversationStatus) => {
      if (this.onStatusCallback) {
        this.onStatusCallback(convId, status);
      }
    });

    this.connection.on('AISuggestionGenerated', (suggestion: AISuggestion) => {
      if (this.onAISuggestionCallback) {
        this.onAISuggestionCallback(suggestion);
      }
    });

    this.connection.on('AITyping', (data: { conversationId: string; isTyping: boolean }) => {
      if (this.onAITypingCallback) {
        this.onAITypingCallback(data.conversationId, data.isTyping);
      }
    });

    this.connection.on('NotificationReceived', (title: string, body: string, type: string) => {
      if (this.onNotificationCallback) {
        this.onNotificationCallback(title, body, type);
      }
    });

    this.connection.on('AgentPresenceUpdated', (agentId: string, status: 'Online' | 'Offline') => {
      if (this.onPresenceCallback) {
        this.onPresenceCallback(agentId, status);
      }
    });

    this.connection.on('CustomerUpdated', (customer: any) => {
      if (this.onCustomerCallback) {
        this.onCustomerCallback(customer);
      }
    });

    try {
      await this.connection.start();
      console.log('SignalR connection established successfully.');
      // Join group for multi-tenant isolation
      await this.connection.invoke('JoinProjectGroup', this.projectId);
    } catch (err) {
      console.error('Error starting SignalR connection:', err);
      const failedConnection = this.connection;
      this.connection = null;
      try {
        await failedConnection?.stop();
      } catch {
        // Ignore cleanup failures after a failed negotiation/start.
      }
      throw err;
    }
  }

  public async stop(): Promise<void> {
    if (!this.connection) return;
    try {
      await this.connection.stop();
      console.log('SignalR connection stopped.');
    } catch (err) {
      console.error('Error stopping SignalR connection:', err);
    } finally {
      this.connection = null;
    }
  }

  public async updatePresence(status: 'Online' | 'Offline'): Promise<void> {
    if (this.connection?.state !== HubConnectionState.Connected) return;
    try {
      await this.connection.invoke('UpdatePresence', status);
    } catch (err) {
      console.error('Error updating presence:', err);
    }
  }

  // Hook registers
  public registerOnMessage(callback: (message: Message) => void) {
    this.onMessageCallback = callback;
  }

  public registerOnStatusChange(callback: (convId: string, status: ConversationStatus) => void) {
    this.onStatusCallback = callback;
  }

  public registerOnAISuggestion(callback: (suggestion: AISuggestion) => void) {
    this.onAISuggestionCallback = callback;
  }

  public registerOnNotification(callback: (title: string, body: string, type: string) => void) {
    this.onNotificationCallback = callback;
  }

  public registerOnPresenceChange(callback: (agentId: string, status: 'Online' | 'Offline') => void) {
    this.onPresenceCallback = callback;
  }

  public registerOnAITyping(callback: (convId: string, isTyping: boolean) => void) {
    this.onAITypingCallback = callback;
  }

  public registerOnCustomerUpdate(callback: (customer: any) => void) {
    this.onCustomerCallback = callback;
  }
}
