import 'dart:convert';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:equatable/equatable.dart';
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

class AuthRegisterRequested extends AuthEvent {
  final String email;
  final String password;
  final String fullName;
  const AuthRegisterRequested({required this.email, required this.password, required this.fullName});
  @override
  List<Object?> get props => [email, password, fullName];
}

class AuthProjectSelected extends AuthEvent {
  final Project project;
  const AuthProjectSelected(this.project);
  @override
  List<Object?> get props => [project];
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
class AuthProjectSelectionRequired extends AuthState {
  final User user;
  final List<Project> projects;
  const AuthProjectSelectionRequired({required this.user, required this.projects});
  @override
  List<Object?> get props => [user, projects];
}
class AuthFailure extends AuthState {
  final String error;
  const AuthFailure(this.error);
  @override
  List<Object?> get props => [error];
}

// BLoC
class AuthBloc extends Bloc<AuthEvent, AuthState> {
  final AuthRepository _authRepository;

  AuthBloc({required AuthRepository authRepository})
      : _authRepository = authRepository,
        super(AuthInitial()) {
    on<AuthCheckStatus>(_onCheckStatus);
    on<AuthLoginRequested>(_onLoginRequested);
    on<AuthRegisterRequested>(_onRegisterRequested);
    on<AuthProjectSelected>(_onProjectSelected);
    on<AuthLogoutRequested>(_onLogout);
  }

  Future<void> _onCheckStatus(AuthCheckStatus event, Emitter<AuthState> emit) async {
    emit(AuthLoading());
    try {
      final cachedProject = await _authRepository.getActiveProject();
      final user = await _authRepository.getAuthenticatedUser();
      if (user != null && cachedProject != null) {
        // Fetch the LATEST project details from the network to avoid cache staleness!
        final latestProject = await _authRepository.getProject(cachedProject.id);
        await _authRepository.setActiveProject(latestProject);
        emit(AuthAuthenticated(user: user, activeProject: latestProject));
      } else {
        emit(AuthUnauthenticated());
      }
    } catch (_) {
      emit(AuthUnauthenticated());
    }
  }

  Future<void> _onLoginRequested(AuthLoginRequested event, Emitter<AuthState> emit) async {
    emit(AuthLoading());
    try {
      final session = await _authRepository.login(event.email, event.password);
      final projects = await _authRepository.getProjects();
      
      if (projects.isEmpty) {
        emit(const AuthFailure('No projects associated with this account.'));
      } else if (projects.length == 1) {
        final fullProject = await _authRepository.getProject(projects.first.id);
        await _authRepository.setActiveProject(fullProject);
        emit(AuthAuthenticated(user: session.user, activeProject: fullProject));
      } else {
        emit(AuthProjectSelectionRequired(user: session.user, projects: projects));
      }
    } catch (e) {
      emit(AuthFailure(e.toString()));
    }
  }

  Future<void> _onRegisterRequested(AuthRegisterRequested event, Emitter<AuthState> emit) async {
    emit(AuthLoading());
    try {
      await _authRepository.register(event.email, event.password, event.fullName);
      // Auto log in after register
      add(AuthLoginRequested(email: event.email, password: event.password));
    } catch (e) {
      emit(AuthFailure(e.toString()));
    }
  }

  Future<void> _onProjectSelected(AuthProjectSelected event, Emitter<AuthState> emit) async {
    emit(AuthLoading());
    try {
      final fullProject = await _authRepository.getProject(event.project.id);
      await _authRepository.setActiveProject(fullProject);
      final user = await _authRepository.getAuthenticatedUser();
      if (user != null) {
        emit(AuthAuthenticated(user: user, activeProject: fullProject));
      } else {
        emit(AuthUnauthenticated());
      }
    } catch (e) {
      emit(AuthFailure(e.toString()));
    }
  }

  Future<void> _onLogout(AuthLogoutRequested event, Emitter<AuthState> emit) async {
    emit(AuthLoading());
    await _authRepository.logout();
    emit(AuthUnauthenticated());
  }
}
