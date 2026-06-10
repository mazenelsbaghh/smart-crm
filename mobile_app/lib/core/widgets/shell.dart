import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import '../theme/colors.dart';
import '../theme/typography.dart';

class AppShell extends StatelessWidget {
  final StatefulNavigationShell navigationShell;

  const AppShell({
    Key? key,
    required this.navigationShell,
  }) : super(key: key ?? const ValueKey<String>('AppShell'));

  void _onTap(BuildContext context, int index) {
    navigationShell.goBranch(
      index,
      initialLocation: index == navigationShell.currentIndex,
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    
    // Check if tablet or landscape for adaptive sidebar layout
    final isWide = MediaQuery.of(context).size.width >= 768;

    return Scaffold(
      backgroundColor: AppColors.background,
      body: Row(
        children: [
          if (isWide) ...[
            _buildSidebar(context),
            const VerticalDivider(
              width: 1,
              thickness: 1,
              color: AppColors.border,
            ),
          ],
          Expanded(
            child: navigationShell,
          ),
        ],
      ),
      bottomNavigationBar: isWide
          ? null
          : Theme(
              data: theme.copyWith(
                splashColor: Colors.transparent,
                highlightColor: Colors.transparent,
              ),
              child: Container(
                decoration: const BoxDecoration(
                  border: Border(
                    top: BorderSide(color: AppColors.border, width: 1),
                  ),
                ),
                child: BottomNavigationBar(
                  currentIndex: navigationShell.currentIndex,
                  onTap: (index) => _onTap(context, index),
                  backgroundColor: AppColors.background,
                  selectedItemColor: AppColors.primary,
                  unselectedItemColor: AppColors.textMuted,
                  selectedLabelStyle: AppTypography.label.copyWith(
                    color: AppColors.primary,
                    fontWeight: FontWeight.bold,
                  ),
                  unselectedLabelStyle: AppTypography.label,
                  type: BottomNavigationBarType.fixed,
                  elevation: 0,
                  items: const [
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
                    BottomNavigationBarItem(
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

  Widget _buildSidebar(BuildContext context) {
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
                'SMART CRM',
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
