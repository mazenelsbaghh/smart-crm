import 'package:dio/dio.dart';
import 'package:equatable/equatable.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/services/user_facing_error.dart';
import '../data/models/user_model.dart';
import '../data/repositories/auth_repository.dart';

// Events
abstract class AuthEvent extends Equatable {
  const AuthEvent();
  @override
  List<Object?> get props => [];
}

class AuthCheckStatus extends AuthEvent {}

class AuthLoginRequested extends AuthEvent {
  final String email;
  final String password;
  const AuthLoginRequested({required this.email, required this.password});
  @override
  List<Object?> get props => [email, password];
}

class AuthLogoutRequested extends AuthEvent {}

// States
abstract class AuthState extends Equatable {
  const AuthState();
  @override
  List<Object?> get props => [];
}

class AuthInitial extends AuthState {}

class AuthLoading extends AuthState {}

class AuthAuthenticated extends AuthState {
  final User user;
  final Project activeProject;
  const AuthAuthenticated({required this.user, required this.activeProject});
  @override
  List<Object?> get props => [user, activeProject];
}

class AuthUnauthenticated extends AuthState {}

class AuthFailure extends AuthState {
  final String error;
  const AuthFailure(this.error);
  @override
  List<Object?> get props => [error];
}

// BLoC
class AuthBloc extends Bloc<AuthEvent, AuthState> {
  final AuthRepository _authRepository;
  int _authOperationGeneration = 0;

  AuthBloc({required AuthRepository authRepository})
    : _authRepository = authRepository,
      super(AuthInitial()) {
    on<AuthCheckStatus>(_onCheckStatus);
    on<AuthLoginRequested>(_onLoginRequested);
    on<AuthLogoutRequested>(_onLogout);
  }

  Future<void> _onCheckStatus(
    AuthCheckStatus event,
    Emitter<AuthState> emit,
  ) async {
    final operationGeneration = ++_authOperationGeneration;
    if (state is! AuthAuthenticated) emit(AuthLoading());
    String? cachedRefreshToken;
    Project? cachedProject;
    User? user;
    try {
      cachedRefreshToken = await _authRepository.getStoredRefreshToken();
      if (!_isCurrent(operationGeneration)) return;
      cachedProject = await _authRepository.getActiveProject();
      if (!_isCurrent(operationGeneration)) return;
      user = await _authRepository.getAuthenticatedUser();
      if (!_isCurrent(operationGeneration)) return;
      if (cachedRefreshToken != null && user != null && cachedProject != null) {
        final latestProject = await _authRepository.getProject(
          cachedProject.id,
        );
        if (!_isCurrent(operationGeneration)) return;
        final projectCached = await _cacheProjectForCurrentSession(
          latestProject,
          operationGeneration,
        );
        if (!_isCurrent(operationGeneration)) return;
        if (!projectCached) {
          emit(AuthUnauthenticated());
          return;
        }
        emit(AuthAuthenticated(user: user, activeProject: latestProject));
      } else {
        emit(AuthUnauthenticated());
      }
    } catch (error) {
      if (!_isCurrent(operationGeneration)) return;
      final statusCode = error is DioException
          ? error.response?.statusCode
          : null;
      if (statusCode == 401 || statusCode == 403) {
        if (cachedRefreshToken != null) {
          await _authRepository.invalidateSession(cachedRefreshToken);
        }
        if (!_isCurrent(operationGeneration)) return;
        emit(AuthUnauthenticated());
      } else if (user != null && cachedProject != null) {
        // Keep a valid cached session usable during a temporary outage.
        emit(AuthAuthenticated(user: user, activeProject: cachedProject));
      } else {
        emit(AuthUnauthenticated());
      }
    }
  }

  Future<void> _onLoginRequested(
    AuthLoginRequested event,
    Emitter<AuthState> emit,
  ) async {
    final operationGeneration = ++_authOperationGeneration;
    emit(AuthLoading());
    AuthSession? session;
    try {
      session = await _authRepository.login(event.email, event.password);
      if (!_isCurrent(operationGeneration)) {
        await _authRepository.invalidateSession(session.refreshToken);
        return;
      }
      await _authRepository.saveSession(session);
      if (!_isCurrent(operationGeneration)) {
        await _authRepository.invalidateSession(session.refreshToken);
        return;
      }
      final projects = await _authRepository.getProjects();
      if (!_isCurrent(operationGeneration)) {
        await _authRepository.invalidateSession(session.refreshToken);
        return;
      }

      if (projects.isEmpty) {
        await _authRepository.invalidateSession(session.refreshToken);
        if (!_isCurrent(operationGeneration)) return;
        emit(const AuthFailure('لا توجد مساحة عمل مرتبطة بهذا الحساب.'));
        return;
      }

      final assignedProject = await _authRepository.getProject(
        projects.first.id,
      );
      if (!_isCurrent(operationGeneration)) {
        await _authRepository.invalidateSession(session.refreshToken);
        return;
      }
      final projectCached = await _cacheProjectForCurrentSession(
        assignedProject,
        operationGeneration,
      );
      if (!_isCurrent(operationGeneration)) {
        await _authRepository.invalidateSession(session.refreshToken);
        return;
      }
      if (!projectCached) {
        throw StateError('The authenticated session changed before setup.');
      }
      emit(
        AuthAuthenticated(user: session.user, activeProject: assignedProject),
      );
    } catch (error) {
      if (!_isCurrent(operationGeneration)) {
        if (session != null) {
          await _authRepository.invalidateSession(session.refreshToken);
        }
        return;
      }
      if (session != null) {
        await _authRepository.invalidateSession(session.refreshToken);
      }
      if (_isCurrent(operationGeneration)) {
        emit(AuthFailure(userFacingError(error)));
      }
    }
  }

  Future<void> _onLogout(
    AuthLogoutRequested event,
    Emitter<AuthState> emit,
  ) async {
    final operationGeneration = ++_authOperationGeneration;
    emit(AuthLoading());
    await _authRepository.logout();
    if (_isCurrent(operationGeneration)) emit(AuthUnauthenticated());
  }

  bool _isCurrent(int operationGeneration) {
    return operationGeneration == _authOperationGeneration;
  }

  Future<bool> _cacheProjectForCurrentSession(
    Project project,
    int operationGeneration,
  ) async {
    // A refresh can rotate the token between the project request and cache write.
    for (var attempt = 0; attempt < 2; attempt++) {
      final refreshToken = await _authRepository.getStoredRefreshToken();
      if (!_isCurrent(operationGeneration) || refreshToken == null) {
        return false;
      }
      final saved = await _authRepository.setActiveProject(
        project,
        refreshToken,
      );
      if (!_isCurrent(operationGeneration)) return false;
      if (saved) return true;
    }
    return false;
  }
}
