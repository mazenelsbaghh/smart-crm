import 'package:flutter_test/flutter_test.dart';
import 'package:mobile_app/features/auth/bloc/auth_bloc.dart';
import 'package:mobile_app/features/auth/data/repositories/auth_repository.dart';
import 'package:mobile_app/features/auth/data/models/user_model.dart';
import 'package:mobile_app/core/services/secure_storage.dart';

class MockSecureStorageService implements SecureStorageService {
  final Map<String, String> _data = {};

  @override
  Future<void> saveTokens({required String accessToken, required String refreshToken}) async {
    _data['accessToken'] = accessToken;
    _data['refreshToken'] = refreshToken;
  }

  @override
  Future<String?> getAccessToken() async => _data['accessToken'];

  @override
  Future<String?> getRefreshToken() async => _data['refreshToken'];

  @override
  Future<void> saveUser(String userJson) async => _data['user'] = userJson;

  @override
  Future<String?> getUser() async => _data['user'];

  @override
  Future<void> saveActiveProject(String projectJson) async => _data['activeProject'] = projectJson;

  @override
  Future<String?> getActiveProject() async => _data['activeProject'];

  @override
  Future<void> clearAll() async => _data.clear();
}

class MockAuthRepository implements AuthRepository {
  @override
  Future<AuthSession> login(String email, String password) async {
    final user = User(id: '123', email: email, fullName: 'Test User', role: 'Agent');
    return AuthSession(accessToken: 'access', refreshToken: 'refresh', user: user);
  }

  @override
  Future<void> register(String email, String password, String fullName) async {}

  @override
  Future<List<Project>> getProjects() async => [];

  @override
  Future<void> setActiveProject(Project project) async {}

  @override
  Future<Project?> getActiveProject() async => null;

  @override
  Future<User?> getAuthenticatedUser() async {
    return User(id: '123', email: 'test@example.com', fullName: 'Test User', role: 'Agent');
  }

  @override
  Future<void> logout() async {}
}

void main() {
  late MockAuthRepository mockAuthRepository;
  late AuthBloc authBloc;

  setUp(() {
    mockAuthRepository = MockAuthRepository();
    authBloc = AuthBloc(authRepository: mockAuthRepository);
  });

  tearDown(() {
    authBloc.close();
  });

  test('initial state is AuthInitial', () {
    expect(authBloc.state, AuthInitial());
  });
}
