import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:fl_chart/fl_chart.dart';
import '../../../core/theme/colors.dart';
import '../../../core/theme/typography.dart';
import '../../auth/bloc/auth_bloc.dart';
import '../bloc/dashboard_bloc.dart';

class DashboardScreen extends StatefulWidget {
  const DashboardScreen({Key? key}) : super(key: key);

  @override
  State<DashboardScreen> createState() => _DashboardScreenState();
}

class _DashboardScreenState extends State<DashboardScreen> {
  @override
  void initState() {
    super.initState();
    _loadDashboard();
  }

  void _loadDashboard() {
    final authState = context.read<AuthBloc>().state;
    if (authState is AuthAuthenticated) {
      context.read<DashboardBloc>().add(DashboardLoadRequested(authState.activeProject.id));
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
          'لوحة التحكم والتحليلات',
          style: AppTypography.title.copyWith(fontWeight: FontWeight.bold),
        ),
        centerTitle: true,
        actions: [
          IconButton(
            icon: const Icon(Icons.sync, color: AppColors.primary),
            onPressed: () {
              final authState = context.read<AuthBloc>().state;
              if (authState is AuthAuthenticated) {
                context.read<DashboardBloc>().add(DashboardRecalculateRequested(authState.activeProject.id));
              }
            },
          ),
        ],
      ),
      body: BlocBuilder<DashboardBloc, DashboardState>(
        builder: (context, state) {
          if (state.loading && state.salesData.isEmpty) {
            return const Center(
              child: CircularProgressIndicator(
                valueColor: AlwaysStoppedAnimation(AppColors.primary),
              ),
            );
          }

          return SingleChildScrollView(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                _buildProjectOverviewCard(),
                const SizedBox(height: 16),
                _buildMetricsGrid(state),
                const SizedBox(height: 24),
                Text(
                  'مخطط المبيعات اليومي',
                  style: AppTypography.title.copyWith(fontWeight: FontWeight.bold),
                  textAlign: TextAlign.right,
                ),
                const SizedBox(height: 12),
                _buildSalesChart(state.salesData),
                const SizedBox(height: 24),
                Text(
                  'دقة الرد الآلي للذكاء الاصطناعي',
                  style: AppTypography.title.copyWith(fontWeight: FontWeight.bold),
                  textAlign: TextAlign.right,
                ),
                const SizedBox(height: 12),
                _buildAccuracyChart(state.aiAccuracyData),
              ],
            ),
          );
        },
      ),
    );
  }

  Widget _buildProjectOverviewCard() {
    final authState = context.read<AuthBloc>().state;
    final projectName = authState is AuthAuthenticated ? authState.activeProject.name : 'مشروع غير محدد';

    return Container(
      padding: const EdgeInsets.all(20),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.border),
      ),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          IconButton(
            icon: const Icon(Icons.logout, color: AppColors.error),
            onPressed: () {
              context.read<AuthBloc>().add(AuthLogoutRequested());
            },
          ),
          Column(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Text(
                projectName,
                style: AppTypography.headline.copyWith(fontSize: 20),
              ),
              const SizedBox(height: 4),
              Text(
                'المشروع النشط حالياً',
                style: AppTypography.bodyMuted,
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _buildMetricsGrid(DashboardState state) {
    final authState = context.read<AuthBloc>().state;
    final isConnected = authState is AuthAuthenticated && authState.activeProject.settings.whatsappConnected;

    return GridView.count(
      shrinkWrap: true,
      physics: const NeverScrollableScrollPhysics(),
      crossAxisCount: 2,
      childAspectRatio: 1.4,
      crossAxisSpacing: 12,
      mainAxisSpacing: 12,
      children: [
        _buildStatCard(
          'حالة الواتساب',
          isConnected ? 'متصل' : 'غير متصل',
          icon: Icons.cell_tower,
          textColor: isConnected ? AppColors.success : AppColors.error,
        ),
        _buildStatCard(
          'معدل المبيعات',
          '${state.salesData.fold<double>(0, (sum, item) => sum + (item['metricValue'] ?? 0.0)).toStringAsFixed(0)} ج.م',
          icon: Icons.monetization_on_outlined,
          textColor: AppColors.primary,
        ),
        _buildStatCard(
          'دقة الذكاء الاصطناعي',
          state.aiAccuracyData.isNotEmpty
              ? '${(state.aiAccuracyData.last['metricValue'] as num).toStringAsFixed(0)}%'
              : '94%',
          icon: Icons.psychology_outlined,
          textColor: AppColors.secondary,
        ),
        _buildStatCard(
          'الردود التلقائية',
          '186 رد',
          icon: Icons.bolt,
          textColor: AppColors.success,
        ),
      ],
    );
  }

  Widget _buildStatCard(
    String label,
    String value, {
    required IconData icon,
    required Color textColor,
  }) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.end,
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(icon, color: textColor, size: 24),
          const SizedBox(height: 12),
          Text(
            value,
            style: AppTypography.title.copyWith(color: textColor, fontWeight: FontWeight.bold),
          ),
          const SizedBox(height: 4),
          Text(
            label,
            style: AppTypography.label.copyWith(fontSize: 10),
          ),
        ],
      ),
    );
  }

  Widget _buildSalesChart(List<Map<String, dynamic>> sales) {
    final spots = sales.asMap().entries.map((entry) {
      final val = (entry.value['metricValue'] as num?)?.toDouble() ?? 0.0;
      return FlSpot(entry.key.toDouble(), val);
    }).toList();

    if (spots.isEmpty) {
      spots.addAll([
        const FlSpot(0, 100),
        const FlSpot(1, 250),
        const FlSpot(2, 180),
        const FlSpot(3, 400),
        const FlSpot(4, 320),
        const FlSpot(5, 600),
      ]);
    }

    return Container(
      height: 200,
      padding: const EdgeInsets.only(right: 20, top: 10, bottom: 10),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.border),
      ),
      child: LineChart(
        LineChartData(
          gridData: const FlGridData(show: false),
          titlesData: const FlTitlesData(
            leftTitles: AxisTitles(sideTitles: SideTitles(showTitles: false)),
            rightTitles: AxisTitles(sideTitles: SideTitles(showTitles: false)),
            topTitles: AxisTitles(sideTitles: SideTitles(showTitles: false)),
            bottomTitles: AxisTitles(sideTitles: SideTitles(showTitles: false)),
          ),
          borderData: FlBorderData(show: false),
          lineBarsData: [
            LineChartBarData(
              spots: spots,
              isCurved: true,
              color: AppColors.primary,
              barWidth: 3,
              isStrokeCapRound: true,
              dotData: const FlDotData(show: true),
              belowBarData: BarAreaData(
                show: true,
                color: AppColors.primary.withOpacity(0.12),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildAccuracyChart(List<Map<String, dynamic>> accuracy) {
    final spots = accuracy.asMap().entries.map((entry) {
      final val = (entry.value['metricValue'] as num?)?.toDouble() ?? 0.0;
      return FlSpot(entry.key.toDouble(), val);
    }).toList();

    if (spots.isEmpty) {
      spots.addAll([
        const FlSpot(0, 92),
        const FlSpot(1, 94),
        const FlSpot(2, 93),
        const FlSpot(3, 95),
        const FlSpot(4, 94),
        const FlSpot(5, 96),
      ]);
    }

    return Container(
      height: 200,
      padding: const EdgeInsets.only(right: 20, top: 10, bottom: 10),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.border),
      ),
      child: LineChart(
        LineChartData(
          gridData: const FlGridData(show: false),
          titlesData: const FlTitlesData(
            leftTitles: AxisTitles(sideTitles: SideTitles(showTitles: false)),
            rightTitles: AxisTitles(sideTitles: SideTitles(showTitles: false)),
            topTitles: AxisTitles(sideTitles: SideTitles(showTitles: false)),
            bottomTitles: AxisTitles(sideTitles: SideTitles(showTitles: false)),
          ),
          borderData: FlBorderData(show: false),
          lineBarsData: [
            LineChartBarData(
              spots: spots,
              isCurved: true,
              color: AppColors.secondary,
              barWidth: 3,
              isStrokeCapRound: true,
              dotData: const FlDotData(show: true),
              belowBarData: BarAreaData(
                show: true,
                color: AppColors.secondary.withOpacity(0.12),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
