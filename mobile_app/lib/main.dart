import 'dart:async';

import 'package:firebase_core/firebase_core.dart';
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/date_symbol_data_local.dart';

import 'core/services/api_client.dart';
import 'core/services/push_notification_service.dart';
import 'core/services/secure_storage.dart';
import 'core/services/signalr_service.dart';
import 'core/theme/app_theme.dart';
import 'core/widgets/notification_banner.dart';
import 'core/widgets/shell.dart';
import 'features/auth/bloc/auth_bloc.dart';
import 'features/auth/data/repositories/auth_repository.dart';
import 'features/auth/presentation/login_screen.dart';
import 'features/auth/presentation/register_screen.dart';
import 'features/bookings/bloc/bookings_bloc.dart';
import 'features/bookings/data/repositories/bookings_repository.dart';
import 'features/bookings/presentation/bookings_calendar_screen.dart';
import 'features/crm/bloc/crm_bloc.dart';
import 'features/crm/data/repositories/crm_repository.dart';
import 'features/crm/presentation/customer_detail_screen.dart';
import 'features/crm/presentation/customer_list_screen.dart';
import 'features/crm/presentation/pipeline_board_screen.dart';
import 'features/dashboard/bloc/dashboard_bloc.dart';
import 'features/dashboard/data/repositories/dashboard_repository.dart';
import 'features/dashboard/presentation/dashboard_screen.dart';
import 'features/inbox/bloc/inbox_bloc.dart';
import 'features/inbox/data/models/chat_models.dart';
import 'features/inbox/data/repositories/chat_repository.dart';
import 'features/inbox/presentation/inbox_list_screen.dart';
import 'features/settings/presentation/settings_screen.dart';

void main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await initializeDateFormatting('ar', null);
  try {
    await Firebase.initializeApp();
  } catch (_) {
    // Push notifications are optional; the rest of the app remains usable.
  }
  runApp(const MyApp());
}

class MyApp extends StatefulWidget {
  const MyApp({super.key});

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
  PushNotificationService? _pushNotificationService;
  String? _lastInitializedProjectId;
  String? _realtimeProjectId;
  int _serviceGeneration = 0;

  @override
  void initState() {
    super.initState();
    _secureStorage = SecureStorageService();
    _apiClient = ApiClient(secureStorage: _secureStorage);
    _signalRService = SignalRService(secureStorage: _secureStorage);

    _authRepository = AuthRepository(
      apiClient: _apiClient,
      secureStorage: _secureStorage,
    );
    _chatRepository = ChatRepository(apiClient: _apiClient);
    _crmRepository = CrmRepository(apiClient: _apiClient);
    _bookingsRepository = BookingsRepository(apiClient: _apiClient);
    _dashboardRepository = DashboardRepository(apiClient: _apiClient);

    _authBloc = AuthBloc(authRepository: _authRepository)
      ..add(AuthCheckStatus());
    _inboxBloc = InboxBloc(chatRepository: _chatRepository);
    _crmBloc = CrmBloc(crmRepository: _crmRepository);
    _bookingsBloc = BookingsBloc(bookingsRepository: _bookingsRepository);
    _dashboardBloc = DashboardBloc(
      dashboardRepository: _dashboardRepository,
      crmRepository: _crmRepository,
    );

    // Bind SignalR callbacks to Bloc events
    _signalRService.onMessageReceived = (msg) {
      if (!_canAcceptRealtimeEvents) return;
      _inboxBloc.add(InboxMessageReceived(Message.fromJson(msg)));
    };
    _signalRService.onAISuggestionGenerated = (sug) {
      if (!_canAcceptRealtimeEvents) return;
      _inboxBloc.add(InboxAISuggestionReceived(AISuggestion.fromJson(sug)));
    };
    _signalRService.onAITyping = (map) {
      if (!_canAcceptRealtimeEvents) return;
      final conversationId = map['conversationId'];
      final isTyping = map['isTyping'];
      if (conversationId is! String ||
          conversationId.isEmpty ||
          isTyping is! bool) {
        return;
      }
      final estimatedSeconds = map['estimatedSeconds'];
      final stage = map['stage'];
      _inboxBloc.add(
        InboxAITypingUpdated(
          conversationId: conversationId,
          isTyping: isTyping,
          countdown: estimatedSeconds is int ? estimatedSeconds : null,
          stage: stage is String ? stage : null,
        ),
      );
    };
    _signalRService.onConversationStatusChanged = (convId, status) {
      if (!_canAcceptRealtimeEvents) return;
      _inboxBloc.add(
        InboxConversationStatusChanged(conversationId: convId, status: status),
      );
    };
    _signalRService.onCustomerUpdated = (cust) {
      if (!_canAcceptRealtimeEvents) return;
      _inboxBloc.add(InboxCustomerUpdated(cust));
    };
    _signalRService.onNotificationReceived = (title, message, type) {
      if (!_canAcceptRealtimeEvents) return;
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

    _authSubscription = _authBloc.stream.listen((state) {
      unawaited(_handleAuthStateChange(state));
    });
    unawaited(_handleAuthStateChange(_authBloc.state));
  }

  Future<void> _handleAuthStateChange(AuthState authState) async {
    _router.refresh();
    if (authState is AuthAuthenticated) {
      final projectId = authState.activeProject.id;
      if (_lastInitializedProjectId != projectId) {
        if (_lastInitializedProjectId != null) _clearFeatureState();
        final generation = ++_serviceGeneration;
        _lastInitializedProjectId = projectId;
        _realtimeProjectId = null;
        _pushNotificationService?.dispose();
        _pushNotificationService = null;
        await _signalRService.stop();
        if (generation != _serviceGeneration ||
            _lastInitializedProjectId != projectId) {
          return;
        }
        final realtimeConnected = await _signalRService.start(
          projectId: projectId,
        );
        if (generation != _serviceGeneration ||
            _lastInitializedProjectId != projectId) {
          return;
        }
        if (realtimeConnected) _realtimeProjectId = projectId;

        final pushService = PushNotificationService(
          apiClient: _apiClient,
          projectId: projectId,
          navigatorKey: _navigatorKey,
          onNavigate: (route) => _router.go(route),
        );
        _pushNotificationService = pushService;
        unawaited(pushService.initialize());
      }
    } else if (authState is AuthUnauthenticated || authState is AuthLoading) {
      _clearFeatureState();
      if (_lastInitializedProjectId != null) {
        _serviceGeneration++;
        _lastInitializedProjectId = null;
        _realtimeProjectId = null;
        _pushNotificationService?.dispose();
        _pushNotificationService = null;
        await _signalRService.stop();
      }
    }
  }

  bool get _canAcceptRealtimeEvents {
    final authState = _authBloc.state;
    return authState is AuthAuthenticated &&
        authState.activeProject.id == _realtimeProjectId &&
        _lastInitializedProjectId == _realtimeProjectId;
  }

  void _clearFeatureState() {
    NotificationBanner.clear();
    _inboxBloc.add(const InboxSessionCleared());
    _crmBloc.add(const CrmSessionCleared());
    _bookingsBloc.add(const BookingsSessionCleared());
    _dashboardBloc.add(const DashboardSessionCleared());
  }

  @override
  void dispose() {
    _authSubscription?.cancel();
    _serviceGeneration++;
    _pushNotificationService?.dispose();
    unawaited(_signalRService.stop());
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
    redirect: (context, state) {
      final authState = _authBloc.state;
      final location = state.matchedLocation;
      final isPublic = location == '/' || location == '/register';

      if (authState is AuthInitial) return null;
      if (authState is AuthLoading) return isPublic ? null : '/';

      if (authState is! AuthAuthenticated) {
        return isPublic ? null : '/';
      }

      if (location == '/' || location == '/register') {
        return '/dashboard';
      }

      if (location == '/settings' && !authState.user.canManageProject) {
        return '/dashboard';
      }

      return null;
    },
    routes: [
      GoRoute(path: '/', builder: (context, state) => const LoginScreen()),
      GoRoute(
        path: '/register',
        builder: (context, state) => const RegisterScreen(),
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
                routes: [
                  GoRoute(
                    path: 'pipeline',
                    builder: (context, state) => const PipelineBoardScreen(),
                  ),
                  GoRoute(
                    path: 'customer/:customerId',
                    builder: (context, state) => CustomerDetailScreen(
                      customerId: state.pathParameters['customerId']!,
                    ),
                  ),
                ],
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
          title: 'سمارت كاستمر',
          debugShowCheckedModeBanner: false,
          locale: const Locale('ar', 'EG'),
          supportedLocales: const [Locale('ar', 'EG')],
          localizationsDelegates: const [
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
          theme: AppTheme.dark,
          themeMode: ThemeMode.dark,
          builder: (context, child) {
            final mediaQuery = MediaQuery.of(context);
            return Directionality(
              textDirection: TextDirection.rtl,
              child: MediaQuery(
                data: mediaQuery.copyWith(
                  textScaler: mediaQuery.textScaler.clamp(
                    minScaleFactor: 1,
                    maxScaleFactor: 2,
                  ),
                ),
                child: child ?? const SizedBox.shrink(),
              ),
            );
          },
          routerConfig: _router,
        ),
      ),
    );
  }
}
