import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:mobile_app/features/auth/bloc/auth_bloc.dart';
import 'package:mobile_app/features/auth/data/models/user_model.dart';
import 'package:mobile_app/features/auth/data/repositories/auth_repository.dart';

class FakeAuthRepository implements AuthRepository {
  User? cachedUser;
  Project? cachedProject;
  List<Project> projects = [];
  Object? projectLoadError;
  String? storedRefreshToken;
  Completer<AuthSession>? loginGate;
  Completer<void>? projectLoadGate;
  Completer<void>? projectLoadStarted;

  @override
  Future<AuthSession> login(String email, String password) async {
    if (loginGate case final gate?) return gate.future;
    return AuthSession(
      accessToken: 'access',
      refreshToken: 'refresh',
      user: cachedUser!,
    );
  }

  @override
  Future<void> saveSession(AuthSession session) async {
    storedRefreshToken = session.refreshToken;
    cachedUser = session.user;
    cachedProject = null;
  }

  @override
  Future<String?> getStoredRefreshToken() async => storedRefreshToken;

  @override
  Future<void> invalidateSession(String refreshToken) async {
    if (storedRefreshToken != refreshToken) return;
    storedRefreshToken = null;
    cachedUser = null;
    cachedProject = null;
  }

  @override
  Future<List<Project>> getProjects() async => projects;

  @override
  Future<bool> setActiveProject(
    Project project,
    String expectedRefreshToken,
  ) async {
    if (storedRefreshToken != expectedRefreshToken) return false;
    cachedProject = project;
    return true;
  }

  @override
  Future<Project?> getActiveProject() async => cachedProject;

  @override
  Future<Project> getProject(String id) async {
    projectLoadStarted?.complete();
    await projectLoadGate?.future;
    if (projectLoadError case final error?) throw error;
    return projects.firstWhere(
      (project) => project.id == id,
      orElse: () => cachedProject!,
    );
  }

  @override
  Future<User?> getAuthenticatedUser() async => cachedUser;

  @override
  Future<void> logout() async {
    storedRefreshToken = null;
    cachedUser = null;
    cachedProject = null;
  }
}

final _user = User(
  id: 'user-1',
  email: 'agent@example.com',
  fullName: 'موظف الاختبار',
  role: 'Agent',
);

Project _project(String id) {
  return Project(
    id: id,
    name: 'مشروع $id',
    whatsappConnected: false,
    settings: ProjectSettings(
      aiAutoReplyEnabled: false,
      timezone: 'Africa/Cairo',
      geminiApiKey: '',
      geminiModel: 'gemini-3.5-flash',
      aiTonePreference: 'واضح',
      aiTargetAudience: 'العملاء',
      replyDelay: 3,
      maxDailyMessages: 100,
      isGroupAppointmentsEnabled: false,
    ),
  );
}

AuthSession _session() {
  return AuthSession(
    accessToken: 'access',
    refreshToken: 'refresh',
    user: _user,
  );
}

void main() {
  late FakeAuthRepository repository;
  late AuthBloc bloc;

  setUp(() {
    repository = FakeAuthRepository();
    bloc = AuthBloc(authRepository: repository);
  });

  tearDown(() async {
    if (!bloc.isClosed) await bloc.close();
  });

  test(
    'temporary sync outage keeps a valid cached session signed in',
    () async {
      repository
        ..cachedUser = _user
        ..cachedProject = _project('project-1')
        ..storedRefreshToken = 'refresh'
        ..projectLoadError = StateError('network unavailable');
      final settled = bloc.stream.firstWhere(
        (state) => state is AuthAuthenticated,
      );

      bloc.add(AuthCheckStatus());
      final state = await settled as AuthAuthenticated;

      expect(state.user.id, _user.id);
      expect(state.activeProject.id, 'project-1');
    },
  );

  test('login activates the single JWT workspace automatically', () async {
    repository
      ..cachedUser = _user
      ..projects = [_project('project-1')];
    final authenticated = bloc.stream.firstWhere(
      (state) => state is AuthAuthenticated,
    );

    bloc.add(
      const AuthLoginRequested(
        email: 'agent@example.com',
        password: 'password',
      ),
    );
    final state = await authenticated as AuthAuthenticated;

    expect(state.activeProject.id, 'project-1');
    expect(repository.cachedProject?.id, 'project-1');
  });

  test('login reports an explicit missing workspace state', () async {
    repository
      ..cachedUser = _user
      ..projects = [];
    final failed = bloc.stream.firstWhere((state) => state is AuthFailure);

    bloc.add(
      const AuthLoginRequested(
        email: 'agent@example.com',
        password: 'password',
      ),
    );
    final state = await failed as AuthFailure;

    expect(state.error, contains('لا توجد مساحة عمل'));
    expect(repository.storedRefreshToken, isNull);
    expect(repository.cachedUser, isNull);
    expect(repository.cachedProject, isNull);
  });

  test('logout wins when an earlier login response arrives late', () async {
    // Regression 2026-08-25: a delayed login must not restore a logged-out user.
    repository.loginGate = Completer<AuthSession>();
    final loading = bloc.stream.firstWhere((state) => state is AuthLoading);
    bloc.add(
      const AuthLoginRequested(
        email: 'agent@example.com',
        password: 'password',
      ),
    );
    await loading;

    final loggedOut = bloc.stream.firstWhere(
      (state) => state is AuthUnauthenticated,
    );
    bloc.add(AuthLogoutRequested());
    await loggedOut;

    repository.loginGate!.complete(_session());
    await bloc.close();

    expect(bloc.state, isA<AuthUnauthenticated>());
    expect(repository.cachedUser, isNull);
    expect(repository.cachedProject, isNull);
  });

  test('logout wins when an earlier session check completes late', () async {
    // Regression 2026-08-25: stale startup sync must not revive a session.
    repository
      ..cachedUser = _user
      ..cachedProject = _project('project-1')
      ..storedRefreshToken = 'refresh'
      ..projects = [_project('project-1')]
      ..projectLoadGate = Completer<void>()
      ..projectLoadStarted = Completer<void>();
    bloc.add(AuthCheckStatus());
    await repository.projectLoadStarted!.future;

    final loggedOut = bloc.stream.firstWhere(
      (state) => state is AuthUnauthenticated,
    );
    bloc.add(AuthLogoutRequested());
    await loggedOut;

    repository.projectLoadGate!.complete();
    await bloc.close();

    expect(bloc.state, isA<AuthUnauthenticated>());
    expect(repository.cachedUser, isNull);
    expect(repository.cachedProject, isNull);
  });

  test('malformed login payload is rejected before a session is created', () {
    // Regression 2026-08-25: missing user data previously became an empty agent.
    expect(
      () => AuthSession.fromJson({
        'accessToken': 'access',
        'refreshToken': 'refresh',
      }),
      throwsFormatException,
    );
  });
}
