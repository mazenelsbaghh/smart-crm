import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/date_symbol_data_local.dart';

import 'core/theme/colors.dart';
import 'core/widgets/shell.dart';
import 'core/widgets/notification_banner.dart';
import 'package:firebase_core/firebase_core.dart';
import 'core/services/push_notification_service.dart';

// Services
import 'core/services/secure_storage.dart';
import 'core/services/api_client.dart';
import 'core/services/signalr_service.dart';

// BLoCs & Repositories
import 'features/auth/bloc/auth_bloc.dart';
import 'features/auth/data/repositories/auth_repository.dart';
import 'features/inbox/bloc/inbox_bloc.dart';
import 'features/inbox/data/models/chat_models.dart';
import 'features/inbox/data/repositories/chat_repository.dart';
import 'features/crm/bloc/crm_bloc.dart';
import 'features/crm/data/repositories/crm_repository.dart';
import 'features/bookings/bloc/bookings_bloc.dart';
import 'features/bookings/data/repositories/bookings_repository.dart';
import 'features/dashboard/bloc/dashboard_bloc.dart';
import 'features/dashboard/data/repositories/dashboard_repository.dart';

// Screens
import 'features/auth/presentation/login_screen.dart';
import 'features/auth/presentation/register_screen.dart';
import 'features/auth/presentation/project_select_screen.dart';
import 'features/dashboard/presentation/dashboard_screen.dart';
import 'features/inbox/presentation/inbox_list_screen.dart';
import 'features/crm/presentation/customer_list_screen.dart';
import 'features/bookings/presentation/bookings_calendar_screen.dart';
import 'features/settings/presentation/settings_screen.dart';

void main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await initializeDateFormatting('ar', null);
  try {
    await Firebase.initializeApp();
    print('✅ Firebase initialized successfully.');
  } catch (e) {
    print('⚠️ Firebase initialization skipped/failed: $e');
  }
  runApp(const MyApp());
}

class MyApp extends StatefulWidget {
  const MyApp({Key? key}) : super(key: key);

  @override
  State<MyApp> createState() => _MyAppState();
}

class _MyAppState extends State<MyApp> {
  late final SecureStorageService _secureStorage;
  late final ApiClient _apiClient;
  late final SignalRService _signalRService;
  
  late final AuthRepository _authRepository;
  late final ChatRepository _chatRepository;
  late final CrmRepository _crmRepository;
  late final BookingsRepository _bookingsRepository;
  late final DashboardRepository _dashboardRepository;

  late final AuthBloc _authBloc;
  late final InboxBloc _inboxBloc;
  late final CrmBloc _crmBloc;
  late final BookingsBloc _bookingsBloc;
  late final DashboardBloc _dashboardBloc;

  final GlobalKey<NavigatorState> _navigatorKey = GlobalKey<NavigatorState>();
  StreamSubscription<AuthState>? _authSubscription;
  String? _lastInitializedProjectId;

  @override
  void initState() {
    super.initState();
    _secureStorage = SecureStorageService();
    _apiClient = ApiClient(secureStorage: _secureStorage);
    _signalRService = SignalRService(secureStorage: _secureStorage);

    _authRepository = AuthRepository(apiClient: _apiClient, secureStorage: _secureStorage);
    _chatRepository = ChatRepository(apiClient: _apiClient);
    _crmRepository = CrmRepository(apiClient: _apiClient);
    _bookingsRepository = BookingsRepository(apiClient: _apiClient);
    _dashboardRepository = DashboardRepository(apiClient: _apiClient);

    _authBloc = AuthBloc(authRepository: _authRepository)..add(AuthCheckStatus());
    _inboxBloc = InboxBloc(chatRepository: _chatRepository);
    _crmBloc = CrmBloc(crmRepository: _crmRepository);
    _bookingsBloc = BookingsBloc(bookingsRepository: _bookingsRepository);
    _dashboardBloc = DashboardBloc(
      dashboardRepository: _dashboardRepository,
      crmRepository: _crmRepository,
    );

    // Bind SignalR callbacks to Bloc events
    _signalRService.onMessageReceived = (msg) {
      _inboxBloc.add(InboxMessageReceived(Message.fromJson(msg)));
    };
    _signalRService.onAISuggestionGenerated = (sug) {
      _inboxBloc.add(InboxAISuggestionReceived(AISuggestion.fromJson(sug)));
    };
    _signalRService.onAITyping = (map) {
      _inboxBloc.add(InboxAITypingUpdated(
        conversationId: map['conversationId'] ?? '',
        isTyping: map['isTyping'] ?? false,
        countdown: map['estimatedSeconds'],
        stage: map['stage'],
      ));
    };
    _signalRService.onConversationStatusChanged = (convId, status) {
      _inboxBloc.add(InboxConversationStatusChanged(conversationId: convId, status: status));
    };
    _signalRService.onCustomerUpdated = (cust) {
      _inboxBloc.add(InboxCustomerUpdated(cust));
    };
    _signalRService.onNotificationReceived = (title, message, type) {
      if (_navigatorKey.currentState != null) {
        NotificationBanner.show(
          navigatorState: _navigatorKey.currentState!,
          title: title,
          message: message,
          type: type,
          onTap: () {
            if (type == 'Booking') {
              _router.go('/bookings');
            }
          },
        );
      }
      if (type == 'Booking') {
        _bookingsBloc.add(BookingsFetchRequested());
      }
    };

    _authSubscription = _authBloc.stream.listen(_handleAuthStateChange);
    _handleAuthStateChange(_authBloc.state);
  }

  void _handleAuthStateChange(AuthState authState) {
    if (authState is AuthAuthenticated) {
      final projectId = authState.activeProject.id;
      if (_lastInitializedProjectId != projectId) {
        _lastInitializedProjectId = projectId;
        _signalRService.start(projectId: projectId);

        final pushService = PushNotificationService(
          apiClient: _apiClient,
          projectId: projectId,
          navigatorKey: _navigatorKey,
          onNavigate: (route) => _router.go(route),
        );
        pushService.initialize();
      }
    } else if (authState is AuthUnauthenticated) {
      if (_lastInitializedProjectId != null) {
        _lastInitializedProjectId = null;
        _signalRService.stop();
        _router.go('/');
      }
    }
  }

  @override
  void dispose() {
    _authSubscription?.cancel();
    _authBloc.close();
    _inboxBloc.close();
    _crmBloc.close();
    _bookingsBloc.close();
    _dashboardBloc.close();
    super.dispose();
  }

  late final GoRouter _router = GoRouter(
    navigatorKey: _navigatorKey,
    initialLocation: '/',
    routes: [
      GoRoute(
        path: '/',
        builder: (context, state) => const LoginScreen(),
      ),
      GoRoute(
        path: '/register',
        builder: (context, state) => const RegisterScreen(),
      ),
      GoRoute(
        path: '/project-select',
        builder: (context, state) => const ProjectSelectScreen(),
      ),
      StatefulShellRoute.indexedStack(
        builder: (context, state, navigationShell) {
          return AppShell(navigationShell: navigationShell);
        },
        branches: [
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/dashboard',
                builder: (context, state) => const DashboardScreen(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/inbox',
                builder: (context, state) => const InboxListScreen(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/crm',
                builder: (context, state) => const CustomerListScreen(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/bookings',
                builder: (context, state) => const BookingsCalendarScreen(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/settings',
                builder: (context, state) => const SettingsScreen(),
              ),
            ],
          ),
        ],
      ),
    ],
  );

  @override
  Widget build(BuildContext context) {
    return MultiRepositoryProvider(
      providers: [
        RepositoryProvider.value(value: _apiClient),
        RepositoryProvider.value(value: _authRepository),
        RepositoryProvider.value(value: _chatRepository),
        RepositoryProvider.value(value: _crmRepository),
        RepositoryProvider.value(value: _bookingsRepository),
        RepositoryProvider.value(value: _dashboardRepository),
      ],
      child: MultiBlocProvider(
        providers: [
          BlocProvider.value(value: _authBloc),
          BlocProvider.value(value: _inboxBloc),
          BlocProvider.value(value: _crmBloc),
          BlocProvider.value(value: _bookingsBloc),
          BlocProvider.value(value: _dashboardBloc),
        ],
        child: MaterialApp.router(
          title: 'Smart CRM',
          debugShowCheckedModeBanner: false,
          theme: ThemeData(
            brightness: Brightness.light,
            scaffoldBackgroundColor: AppColors.background,
            primaryColor: AppColors.primary,
          ),
          routerConfig: _router,
        ),
      ),
    );
  }
}
