import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../features/auth/bloc/auth_bloc.dart';
import '../theme/colors.dart';
import '../theme/typography.dart';

class AppShell extends StatelessWidget {
  const AppShell({super.key, required this.navigationShell});

  final StatefulNavigationShell navigationShell;

  void _onTap(BuildContext context, int index) {
    navigationShell.goBranch(
      index,
      initialLocation: index == navigationShell.currentIndex,
    );
  }

  @override
  Widget build(BuildContext context) {
    final isWide = MediaQuery.of(context).size.width >= 768;
    final authState = context.watch<AuthBloc>().state;
    final canManage =
        authState is AuthAuthenticated && authState.user.canManageProject;
    final visibleBranches = canManage
        ? const [0, 1, 2, 3, 4]
        : const [0, 1, 2, 3];
    final visibleIndex = visibleBranches.indexOf(navigationShell.currentIndex);

    return Scaffold(
      backgroundColor: AppColors.background,
      body: Row(
        children: [
          if (isWide) ...[
            _buildSidebar(context, canManage: canManage),
            const VerticalDivider(
              width: 1,
              thickness: 1,
              color: AppColors.border,
            ),
          ],
          Expanded(child: navigationShell),
        ],
      ),
      bottomNavigationBar: isWide
          ? null
          : SafeArea(
              top: false,
              child: Container(
                decoration: const BoxDecoration(
                  border: Border(
                    top: BorderSide(color: AppColors.border, width: 1),
                  ),
                ),
                child: BottomNavigationBar(
                  currentIndex: visibleIndex < 0 ? 0 : visibleIndex,
                  onTap: (index) => _onTap(context, visibleBranches[index]),
                  backgroundColor: AppColors.surface,
                  selectedItemColor: AppColors.primary,
                  unselectedItemColor: AppColors.textMuted,
                  selectedLabelStyle: AppTypography.label.copyWith(
                    color: AppColors.primary,
                    fontWeight: FontWeight.bold,
                  ),
                  unselectedLabelStyle: AppTypography.label,
                  type: BottomNavigationBarType.fixed,
                  elevation: 0,
                  items: [
                    BottomNavigationBarItem(
                      icon: Icon(Icons.dashboard_outlined),
                      activeIcon: Icon(Icons.dashboard),
                      label: 'الرئيسية',
                    ),
                    BottomNavigationBarItem(
                      icon: Icon(Icons.chat_bubble_outline),
                      activeIcon: Icon(Icons.chat_bubble),
                      label: 'المحادثات',
                    ),
                    BottomNavigationBarItem(
                      icon: Icon(Icons.people_outline),
                      activeIcon: Icon(Icons.people),
                      label: 'العملاء',
                    ),
                    BottomNavigationBarItem(
                      icon: Icon(Icons.calendar_month_outlined),
                      activeIcon: Icon(Icons.calendar_month),
                      label: 'المواعيد',
                    ),
                    if (canManage)
                      const BottomNavigationBarItem(
                        icon: Icon(Icons.settings_outlined),
                        activeIcon: Icon(Icons.settings),
                        label: 'الإعدادات',
                      ),
                  ],
                ),
              ),
            ),
    );
  }

  Widget _buildSidebar(BuildContext context, {required bool canManage}) {
    return Container(
      width: 250,
      color: AppColors.surface,
      child: Column(
        children: [
          DrawerHeader(
            decoration: const BoxDecoration(
              border: Border(
                bottom: BorderSide(color: AppColors.border, width: 1),
              ),
            ),
            child: Center(
              child: Text(
                'سمارت كاستمر',
                style: AppTypography.headline.copyWith(
                  color: AppColors.primary,
                  letterSpacing: 2,
                ),
              ),
            ),
          ),
          _buildSidebarItem(
            context,
            icon: Icons.dashboard_outlined,
            activeIcon: Icons.dashboard,
            label: 'لوحة التحكم',
            index: 0,
          ),
          _buildSidebarItem(
            context,
            icon: Icons.chat_bubble_outline,
            activeIcon: Icons.chat_bubble,
            label: 'المحادثات الواردة',
            index: 1,
          ),
          _buildSidebarItem(
            context,
            icon: Icons.people_outline,
            activeIcon: Icons.people,
            label: 'دليل العملاء',
            index: 2,
          ),
          _buildSidebarItem(
            context,
            icon: Icons.calendar_month_outlined,
            activeIcon: Icons.calendar_month,
            label: 'حجز المواعيد',
            index: 3,
          ),
          if (canManage)
            _buildSidebarItem(
              context,
              icon: Icons.settings_outlined,
              activeIcon: Icons.settings,
              label: 'إعدادات النظام',
              index: 4,
            ),
        ],
      ),
    );
  }

  Widget _buildSidebarItem(
    BuildContext context, {
    required IconData icon,
    required IconData activeIcon,
    required String label,
    required int index,
  }) {
    final isActive = navigationShell.currentIndex == index;
    return ListTile(
      leading: Icon(
        isActive ? activeIcon : icon,
        color: isActive ? AppColors.primary : AppColors.textMuted,
      ),
      title: Text(
        label,
        style: AppTypography.body.copyWith(
          color: isActive ? AppColors.primary : AppColors.text,
          fontWeight: isActive ? FontWeight.bold : FontWeight.normal,
        ),
      ),
      selected: isActive,
      onTap: () => _onTap(context, index),
    );
  }
}
