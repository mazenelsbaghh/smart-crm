import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:intl/intl.dart';

import '../../../core/theme/colors.dart';
import '../../../core/theme/typography.dart';
import '../../../core/widgets/async_state_view.dart';
import '../../auth/bloc/auth_bloc.dart';
import '../bloc/dashboard_bloc.dart';

class DashboardScreen extends StatefulWidget {
  const DashboardScreen({super.key});

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
      context.read<DashboardBloc>().add(
        DashboardLoadRequested(authState.activeProject.id),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final dashboardLoading = context.select(
      (DashboardBloc bloc) => bloc.state.loading,
    );
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
            tooltip: 'تحديث لوحة التحكم',
            icon: const Icon(Icons.refresh, color: AppColors.primary),
            onPressed: dashboardLoading ? null : _loadDashboard,
          ),
        ],
      ),
      body: BlocBuilder<DashboardBloc, DashboardState>(
        builder: (context, state) {
          if (state.loading && !state.hasLoaded) {
            return const AppLoadingSkeleton(rows: 6);
          }
          if (state.error != null && !state.hasLoaded) {
            return AppStateView(
              icon: Icons.cloud_off_outlined,
              title: 'تعذر تحميل لوحة التحكم',
              message: state.error!,
              actionLabel: 'إعادة المحاولة',
              onAction: _loadDashboard,
            );
          }

          return SingleChildScrollView(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (state.loading) ...[
                  Semantics(
                    label: 'جارٍ تحديث لوحة التحكم',
                    liveRegion: true,
                    child: LinearProgressIndicator(),
                  ),
                  const SizedBox(height: 12),
                ],
                if (state.error != null) ...[
                  _buildStaleDataWarning(state.error!),
                  const SizedBox(height: 16),
                ],
                _buildProjectOverviewCard(state),
                const SizedBox(height: 16),
                _buildMetricsGrid(state),
                const SizedBox(height: 24),
                Text(
                  'مخطط المبيعات اليومي',
                  style: AppTypography.title.copyWith(
                    fontWeight: FontWeight.bold,
                  ),
                  textAlign: TextAlign.right,
                ),
                const SizedBox(height: 12),
                _buildMetricChart(
                  state.salesData,
                  available: state.salesAvailable,
                  emptyMessage: 'لا توجد بيانات مبيعات للفترة المتاحة.',
                  unavailableMessage: 'تعذر تحديث بيانات المبيعات.',
                  semanticName: 'المبيعات اليومية',
                  color: AppColors.primary,
                  valueLabel: (value) => NumberFormat.currency(
                    locale: 'ar_EG',
                    symbol: 'ج.م',
                    decimalDigits: 0,
                  ).format(value),
                ),
                const SizedBox(height: 24),
                Text(
                  'دقة الرد الآلي للذكاء الاصطناعي',
                  style: AppTypography.title.copyWith(
                    fontWeight: FontWeight.bold,
                  ),
                  textAlign: TextAlign.right,
                ),
                const SizedBox(height: 12),
                _buildMetricChart(
                  state.aiAccuracyData,
                  available: state.aiAccuracyAvailable,
                  emptyMessage: 'لا توجد قياسات موثقة لدقة الرد الآلي.',
                  unavailableMessage: 'تعذر تحديث قياسات دقة الرد الآلي.',
                  semanticName: 'دقة الرد الآلي',
                  color: AppColors.secondary,
                  valueLabel: (value) => '${value.toStringAsFixed(0)}٪',
                ),
              ],
            ),
          );
        },
      ),
    );
  }

  Widget _buildStaleDataWarning(String message) {
    return Semantics(
      liveRegion: true,
      child: Container(
        padding: const EdgeInsets.all(12),
        decoration: BoxDecoration(
          color: AppColors.warning.withValues(alpha: 0.1),
          border: Border.all(color: AppColors.warning.withValues(alpha: 0.55)),
          borderRadius: BorderRadius.circular(8),
        ),
        child: Row(
          children: [
            const Icon(Icons.warning_amber_rounded, color: AppColors.warning),
            const SizedBox(width: 10),
            Expanded(
              child: Text(
                'نعرض آخر بيانات متاحة. $message',
                style: AppTypography.body,
              ),
            ),
            IconButton(
              tooltip: 'إعادة المحاولة',
              onPressed: _loadDashboard,
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildProjectOverviewCard(DashboardState state) {
    final authState = context.read<AuthBloc>().state;
    final projectName = authState is AuthAuthenticated
        ? authState.activeProject.name
        : 'مشروع غير محدد';
    final connectionStatus = state.whatsappConnected;

    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.border),
      ),
      child: Wrap(
        alignment: WrapAlignment.spaceBetween,
        crossAxisAlignment: WrapCrossAlignment.center,
        spacing: 12,
        runSpacing: 12,
        children: [
          IconButton(
            icon: const Icon(Icons.logout, color: AppColors.error),
            tooltip: 'تسجيل الخروج',
            onPressed: () {
              context.read<AuthBloc>().add(AuthLogoutRequested());
            },
          ),
          Row(
            children: [
              Container(
                width: 8,
                height: 8,
                decoration: BoxDecoration(
                  color: connectionStatus == null
                      ? AppColors.textMuted
                      : connectionStatus
                      ? AppColors.success
                      : AppColors.error,
                  shape: BoxShape.circle,
                ),
              ),
              const SizedBox(width: 8),
              Text(
                connectionStatus == null
                    ? 'الحالة غير متاحة'
                    : connectionStatus
                    ? 'متصل'
                    : 'غير متصل',
                style: AppTypography.bodyMuted.copyWith(
                  fontSize: 12,
                  color: connectionStatus == null
                      ? AppColors.textMuted
                      : connectionStatus
                      ? AppColors.success
                      : AppColors.error,
                  fontWeight: FontWeight.bold,
                ),
              ),
              const SizedBox(width: 4),
              const Icon(
                Icons.cell_tower,
                size: 14,
                color: AppColors.textMuted,
              ),
            ],
          ),
          ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 280),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Text(
                  projectName,
                  style: AppTypography.headline.copyWith(
                    fontSize: 18,
                    fontWeight: FontWeight.bold,
                  ),
                ),
                const SizedBox(height: 4),
                Text('المشروع النشط حالياً', style: AppTypography.bodyMuted),
                if (state.lastUpdatedAt != null) ...[
                  const SizedBox(height: 2),
                  Text(
                    'آخر تحديث ${DateFormat('d MMM، h:mm a', 'ar').format(state.lastUpdatedAt!.toLocal())}',
                    style: AppTypography.label,
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildMetricsGrid(DashboardState state) {
    final integer = NumberFormat.decimalPattern('ar_EG');
    final currency = NumberFormat.currency(
      locale: 'ar_EG',
      symbol: 'ج.م',
      decimalDigits: 0,
    );
    final cards = [
      _buildStatCard(
        'إجمالي العملاء',
        state.customersAvailable
            ? integer.format(state.totalCustomers)
            : 'غير متاح',
        icon: Icons.people_outline,
        textColor: AppColors.primary,
      ),
      _buildStatCard(
        'الصفقات المفتوحة',
        state.dealsAvailable ? integer.format(state.activeDeals) : 'غير متاح',
        icon: Icons.track_changes,
        textColor: AppColors.secondary,
      ),
      _buildStatCard(
        'الإيراد المغلق',
        state.dealsAvailable
            ? currency.format(state.closedWonRevenue)
            : 'غير متاح',
        icon: Icons.monetization_on_outlined,
        textColor: AppColors.success,
      ),
      _buildStatCard(
        'متوسط التقييم',
        state.customersAvailable
            ? '${integer.format(state.avgLeadScore)}/100'
            : 'غير متاح',
        icon: Icons.trending_up,
        textColor: AppColors.warning,
      ),
    ];
    return LayoutBuilder(
      builder: (context, constraints) {
        final scaledBody = MediaQuery.textScalerOf(context).scale(14);
        final oneColumn = constraints.maxWidth < 340 || scaledBody > 19;
        final cardWidth = oneColumn
            ? constraints.maxWidth
            : (constraints.maxWidth - 12) / 2;
        return Wrap(
          spacing: 12,
          runSpacing: 12,
          children: cards
              .map(
                (card) => SizedBox(width: cardWidth, height: 128, child: card),
              )
              .toList(),
        );
      },
    );
  }

  Widget _buildStatCard(
    String label,
    String value, {
    required IconData icon,
    required Color textColor,
  }) {
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.end,
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(icon, color: textColor, size: 22),
          const SizedBox(height: 8),
          Text(
            value,
            style: AppTypography.title.copyWith(
              color: textColor,
              fontWeight: FontWeight.bold,
              fontSize: 16,
            ),
          ),
          const SizedBox(height: 2),
          Text(label, style: AppTypography.label),
        ],
      ),
    );
  }

  Widget _buildMetricChart(
    List<Map<String, dynamic>> data, {
    required bool available,
    required String emptyMessage,
    required String unavailableMessage,
    required String semanticName,
    required Color color,
    required String Function(double value) valueLabel,
  }) {
    if (!available) {
      return _unavailableChart(unavailableMessage);
    }

    var points = <({DateTime? date, double value})>[];
    for (final item in data) {
      final rawValue = item['metricValue'] ?? item['value'];
      if (rawValue is! num || !rawValue.toDouble().isFinite) continue;
      points.add((
        date: DateTime.tryParse(
          (item['snapshotDate'] ?? item['date'])?.toString() ?? '',
        ),
        value: rawValue.toDouble(),
      ));
    }
    if (points.every((point) => point.date != null)) {
      points.sort((a, b) => a.date!.compareTo(b.date!));
    } else {
      // The API returns newest first; keep the visual timeline chronological.
      points = points.reversed.toList();
    }

    if (points.isEmpty) return _emptyChart(emptyMessage);

    final spots = points.indexed
        .map((entry) => FlSpot(entry.$1.toDouble(), entry.$2.value))
        .toList();
    final values = points.map((point) => point.value);
    final minimum = values.reduce((a, b) => a < b ? a : b);
    final maximum = values.reduce((a, b) => a > b ? a : b);
    final firstDate = points.first.date;
    final lastDate = points.last.date;
    final dateFormat = DateFormat('d/M', 'ar');
    final periodLabel = firstDate == null || lastDate == null
        ? '${points.length} قياسات مرتبة زمنيًا'
        : 'من ${dateFormat.format(firstDate.toLocal())} إلى ${dateFormat.format(lastDate.toLocal())}';
    final semanticLabel =
        '$semanticName، $periodLabel، أدنى قيمة ${valueLabel(minimum)}، وأعلى قيمة ${valueLabel(maximum)}.';
    final compactNumber = NumberFormat.compact(locale: 'ar_EG');
    final isPercentage = semanticName.contains('دقة');

    Widget leftTitle(double value, TitleMeta meta) => Padding(
      padding: const EdgeInsetsDirectional.only(end: 4),
      child: Text(
        isPercentage
            ? '${value.toStringAsFixed(0)}٪'
            : compactNumber.format(value),
        style: AppTypography.label,
      ),
    );

    Widget bottomTitle(double value, TitleMeta meta) {
      final index = value.round();
      final middle = (points.length - 1) ~/ 2;
      if (index < 0 ||
          index >= points.length ||
          value != index ||
          (index != 0 && index != middle && index != points.length - 1) ||
          points[index].date == null) {
        return const SizedBox.shrink();
      }
      return Padding(
        padding: const EdgeInsets.only(top: 6),
        child: Text(
          dateFormat.format(points[index].date!.toLocal()),
          style: AppTypography.label,
        ),
      );
    }

    return Semantics(
      label: semanticLabel,
      child: Container(
        height: 260,
        padding: const EdgeInsetsDirectional.fromSTEB(8, 16, 12, 12),
        decoration: BoxDecoration(
          color: AppColors.surface,
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: AppColors.border),
        ),
        child: ExcludeSemantics(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: LineChart(
                  LineChartData(
                    gridData: FlGridData(
                      show: true,
                      drawVerticalLine: false,
                      getDrawingHorizontalLine: (value) =>
                          const FlLine(color: AppColors.border, strokeWidth: 1),
                    ),
                    titlesData: FlTitlesData(
                      leftTitles: AxisTitles(
                        sideTitles: SideTitles(
                          showTitles: true,
                          reservedSize: 48,
                          getTitlesWidget: leftTitle,
                        ),
                      ),
                      rightTitles: const AxisTitles(
                        sideTitles: SideTitles(showTitles: false),
                      ),
                      topTitles: const AxisTitles(
                        sideTitles: SideTitles(showTitles: false),
                      ),
                      bottomTitles: AxisTitles(
                        sideTitles: SideTitles(
                          showTitles: true,
                          reservedSize: 32,
                          interval: 1,
                          getTitlesWidget: bottomTitle,
                        ),
                      ),
                    ),
                    borderData: FlBorderData(
                      show: true,
                      border: const Border(
                        left: BorderSide(color: AppColors.border),
                        bottom: BorderSide(color: AppColors.border),
                      ),
                    ),
                    lineBarsData: [
                      LineChartBarData(
                        spots: spots,
                        isCurved: false,
                        color: color,
                        barWidth: 3,
                        isStrokeCapRound: true,
                        dotData: FlDotData(show: points.length <= 12),
                        belowBarData: BarAreaData(
                          show: true,
                          color: color.withValues(alpha: 0.1),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: 6),
              Text(periodLabel, style: AppTypography.label),
            ],
          ),
        ),
      ),
    );
  }

  Widget _unavailableChart(String message) {
    return SizedBox(
      height: 170,
      child: AppStateView(
        icon: Icons.cloud_off_outlined,
        title: 'البيانات غير متاحة',
        message: message,
        actionLabel: 'إعادة المحاولة',
        onAction: _loadDashboard,
      ),
    );
  }

  Widget _emptyChart(String message) {
    return SizedBox(
      height: 170,
      child: AppStateView(
        icon: Icons.query_stats_outlined,
        title: 'لا توجد بيانات كافية',
        message: message,
      ),
    );
  }
}
