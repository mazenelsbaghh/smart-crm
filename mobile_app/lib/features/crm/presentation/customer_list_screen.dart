import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/colors.dart';
import '../../../core/theme/typography.dart';
import '../../../core/widgets/async_state_view.dart';
import '../../auth/bloc/auth_bloc.dart';
import '../bloc/crm_bloc.dart';
import '../data/models/crm_models.dart';

class CustomerListScreen extends StatefulWidget {
  const CustomerListScreen({super.key});

  @override
  State<CustomerListScreen> createState() => _CustomerListScreenState();
}

class _CustomerListScreenState extends State<CustomerListScreen> {
  final _searchController = TextEditingController();
  String _searchQuery = '';

  @override
  void initState() {
    super.initState();
    _fetchCustomers();
  }

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  void _fetchCustomers() {
    final authState = context.read<AuthBloc>().state;
    if (authState is AuthAuthenticated) {
      context.read<CrmBloc>().add(
        CrmCustomersFetchRequested(authState.activeProject.id),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: AppColors.background,
      appBar: AppBar(
        backgroundColor: AppColors.surface,
        elevation: 0,
        title: Text(
          'دليل العملاء',
          style: AppTypography.title.copyWith(fontWeight: FontWeight.bold),
        ),
        centerTitle: true,
        actions: [
          IconButton(
            tooltip: 'فتح مراحل المبيعات',
            icon: const Icon(Icons.account_tree_outlined),
            onPressed: () => context.push('/crm/pipeline'),
          ),
          IconButton(
            tooltip: 'تحديث العملاء',
            icon: const Icon(Icons.refresh, color: AppColors.primary),
            onPressed: _fetchCustomers,
          ),
        ],
      ),
      body: Column(
        children: [
          _buildSearchBox(),
          Expanded(
            child: BlocBuilder<CrmBloc, CrmState>(
              builder: (context, state) {
                if (state.loadingCustomers && state.customers.isEmpty) {
                  return const AppLoadingSkeleton(rows: 7);
                }
                if (state.error != null && state.customers.isEmpty) {
                  return AppStateView(
                    icon: Icons.cloud_off_outlined,
                    title: 'تعذر تحميل العملاء',
                    message: state.error!,
                    actionLabel: 'إعادة المحاولة',
                    onAction: _fetchCustomers,
                  );
                }

                var list = state.customers;
                if (_searchQuery.isNotEmpty) {
                  list = list.where((c) {
                    return c.name.toLowerCase().contains(
                          _searchQuery.toLowerCase(),
                        ) ||
                        c.phoneNumber.contains(_searchQuery) ||
                        c.city.toLowerCase().contains(
                          _searchQuery.toLowerCase(),
                        );
                  }).toList();
                }

                if (list.isEmpty) {
                  return AppStateView(
                    icon: Icons.people_outline,
                    title: _searchQuery.isEmpty
                        ? 'لا يوجد عملاء بعد'
                        : 'لا توجد نتائج مطابقة',
                    message: _searchQuery.isEmpty
                        ? 'سيظهر العملاء هنا بعد بدء المحاداثات.'
                        : 'جرّب كلمة بحث مختلفة.',
                    actionLabel: _searchQuery.isEmpty ? 'تحديث' : 'مسح البحث',
                    onAction: () {
                      if (_searchQuery.isNotEmpty) {
                        _searchController.clear();
                        setState(() => _searchQuery = '');
                      } else {
                        _fetchCustomers();
                      }
                    },
                  );
                }

                return RefreshIndicator(
                  onRefresh: () async => _fetchCustomers(),
                  child: ListView.separated(
                    physics: const AlwaysScrollableScrollPhysics(),
                    padding: const EdgeInsets.all(16),
                    itemCount: list.length,
                    separatorBuilder: (context, index) =>
                        const SizedBox(height: 12),
                    itemBuilder: (context, index) {
                      final customer = list[index];
                      return _buildCustomerCard(context, customer);
                    },
                  ),
                );
              },
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildSearchBox() {
    return Container(
      color: AppColors.surface,
      padding: const EdgeInsets.all(16),
      child: TextField(
        controller: _searchController,
        style: AppTypography.body,
        textAlign: TextAlign.right,
        decoration: InputDecoration(
          labelText: 'البحث في العملاء',
          hintText: 'ابحث بالاسم، رقم الهاتف، أو المدينة...',
          hintStyle: AppTypography.bodyMuted,
          prefixIcon: const Icon(Icons.search, color: AppColors.textMuted),
          filled: true,
          fillColor: AppColors.background,
          contentPadding: const EdgeInsets.symmetric(vertical: 8),
          border: OutlineInputBorder(
            borderRadius: BorderRadius.circular(8),
            borderSide: const BorderSide(color: AppColors.border),
          ),
          enabledBorder: OutlineInputBorder(
            borderRadius: BorderRadius.circular(8),
            borderSide: const BorderSide(color: AppColors.border),
          ),
          focusedBorder: OutlineInputBorder(
            borderRadius: BorderRadius.circular(8),
            borderSide: const BorderSide(color: AppColors.primary),
          ),
        ),
        onChanged: (val) {
          setState(() {
            _searchQuery = val;
          });
        },
      ),
    );
  }

  Widget _buildCustomerCard(BuildContext context, Customer customer) {
    final safeName = customer.name.trim().isEmpty
        ? 'عميل بلا اسم'
        : customer.name;
    return Semantics(
      button: true,
      label: 'فتح بيانات $safeName',
      child: Material(
        color: AppColors.surface,
        shape: RoundedRectangleBorder(
          side: const BorderSide(color: AppColors.border),
          borderRadius: BorderRadius.circular(12),
        ),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: () =>
              context.push('/crm/customer/${Uri.encodeComponent(customer.id)}'),
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Container(
                  width: 48,
                  height: 48,
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    color: AppColors.background,
                    border: Border.all(color: AppColors.border),
                  ),
                  child: const Icon(Icons.person, color: AppColors.primary),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(safeName, style: AppTypography.title),
                      if (customer.phoneNumber.isNotEmpty) ...[
                        const SizedBox(height: 4),
                        Text(customer.phoneNumber, style: AppTypography.mono),
                      ],
                      if (customer.city.isNotEmpty) ...[
                        const SizedBox(height: 4),
                        Text(customer.city, style: AppTypography.bodyMuted),
                      ],
                      const SizedBox(height: 10),
                      Wrap(
                        spacing: 8,
                        runSpacing: 8,
                        children: [
                          _buildCustomerBadge(
                            icon: Icons.star_outline,
                            label: 'تقييم ${customer.leadScore}',
                            color: AppColors.primary,
                          ),
                          if (customer.isBlacklisted)
                            _buildCustomerBadge(
                              icon: Icons.smart_toy_outlined,
                              label: 'الرد الآلي محظور',
                              color: AppColors.error,
                            ),
                        ],
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 8),
                const Icon(Icons.chevron_left, color: AppColors.textMuted),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildCustomerBadge({
    required IconData icon,
    required String label,
    required Color color,
  }) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.12),
        border: Border.all(color: color.withValues(alpha: 0.3)),
        borderRadius: BorderRadius.circular(6),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, color: color, size: 16),
          const SizedBox(width: 4),
          Text(
            label,
            style: AppTypography.label.copyWith(
              color: color,
              fontWeight: FontWeight.bold,
            ),
          ),
        ],
      ),
    );
  }
}
