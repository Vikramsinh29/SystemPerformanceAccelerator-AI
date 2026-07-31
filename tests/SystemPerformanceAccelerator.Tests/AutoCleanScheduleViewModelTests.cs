using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Services;
using SystemPerformanceAccelerator.Desktop.ViewModels;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class AutoCleanScheduleViewModelTests
{
    [Fact]
    public async Task RunNow_DisabledScheduleCreatesFreshUnselectedReview()
    {
        var schedule = CreateSchedule(isEnabled: false);
        var scheduleService = new FakeScheduleService(schedule);
        var cleanService = new FakeCustomCleanService
        {
            PreviewResult = CreatePreviewResult()
        };
        var viewModel = CreateViewModel(scheduleService, cleanService);
        var selected = Assert.IsType<AutoCleanScheduleItemViewModel>(viewModel.SelectedSchedule);

        Assert.True(viewModel.RunSelectedScheduleCommand.CanExecute(selected));
        await viewModel.RunSelectedScheduleCommand.ExecuteAsync(selected);

        Assert.True(viewModel.IsRunReviewVisible);
        Assert.Single(viewModel.RunResults);
        Assert.All(viewModel.RunResults, item => Assert.False(item.IsSelected));
        Assert.Equal("0", viewModel.RunSelectedFiles);
        Assert.True(viewModel.IsRunPreviewFresh);
        Assert.False(viewModel.CleanRunPreviewCommand.CanExecute(null));
        Assert.Equal(1, cleanService.PreviewCallCount);
    }

    [Fact]
    public async Task CleanRun_WhenConfirmationDeclined_DoesNotCallCleanup()
    {
        var scheduleService = new FakeScheduleService(CreateSchedule());
        var cleanService = new FakeCustomCleanService
        {
            PreviewResult = CreatePreviewResult()
        };
        var confirmation = new FakeRunConfirmationService(false);
        var viewModel = CreateViewModel(
            scheduleService,
            cleanService,
            confirmation);

        await RunPreviewAsync(viewModel);
        viewModel.RunResults[0].IsSelected = true;
        await viewModel.CleanRunPreviewCommand.ExecuteAsync(null);

        Assert.Equal(0, cleanService.CleanCallCount);
        Assert.Contains("not started", viewModel.Status);
        Assert.True(viewModel.IsRunPreviewFresh);
    }

    [Fact]
    public async Task CleanRun_SuccessSavesSummaryAndInvalidatesPreview()
    {
        var completedAt = new DateTime(2026, 7, 31, 14, 45, 0);
        var scheduleService = new FakeScheduleService(CreateSchedule());
        var cleanService = new FakeCustomCleanService
        {
            PreviewResult = CreatePreviewResult(),
            CleanResult = new CustomCleanExecutionResult(
                1,
                1,
                0,
                0,
                2048,
                [],
                TimeSpan.FromMilliseconds(250))
        };
        var viewModel = CreateViewModel(
            scheduleService,
            cleanService,
            nowProvider: () => completedAt);

        await RunPreviewAsync(viewModel);
        viewModel.RunResults[0].IsSelected = true;
        await viewModel.CleanRunPreviewCommand.ExecuteAsync(null);

        var saved = Assert.Single(scheduleService.Schedules);
        var summary = Assert.IsType<AutoCleanManualRunSummary>(saved.LastManualRun);
        Assert.Equal(completedAt, summary.CompletedAtLocal);
        Assert.Equal(1, summary.DeletedCount);
        Assert.Equal(2048, summary.ReclaimedBytes);
        Assert.True(summary.CompletedWithoutIssues);
        Assert.False(viewModel.IsRunPreviewFresh);
        Assert.False(viewModel.CleanRunPreviewCommand.CanExecute(null));
        Assert.True(viewModel.RunOperationResult.IsVisible);
    }

    [Fact]
    public async Task CleanRun_PartialResultIsReportedAndSavedHonestly()
    {
        var scheduleService = new FakeScheduleService(CreateSchedule());
        var cleanService = new FakeCustomCleanService
        {
            PreviewResult = CreatePreviewResult(),
            CleanResult = new CustomCleanExecutionResult(
                1,
                0,
                1,
                0,
                0,
                ["The file changed after preview."],
                TimeSpan.FromSeconds(1))
        };
        var viewModel = CreateViewModel(scheduleService, cleanService);

        await RunPreviewAsync(viewModel);
        viewModel.RunResults[0].IsSelected = true;
        await viewModel.CleanRunPreviewCommand.ExecuteAsync(null);

        var summary = Assert.IsType<AutoCleanManualRunSummary>(
            Assert.Single(scheduleService.Schedules).LastManualRun);
        Assert.Equal(1, summary.SkippedCount);
        Assert.False(summary.CompletedWithoutIssues);
        Assert.Equal("The file changed after preview.", summary.FirstIssue);
        Assert.Contains("skipped or failed", viewModel.Status);
        Assert.Equal("1", viewModel.RunOperationResult.SkippedValue);
    }

    [Fact]
    public async Task RunNow_WhenPreviewIsCancelled_LeavesNoFreshCleanupRequest()
    {
        var scheduleService = new FakeScheduleService(CreateSchedule());
        var cleanService = new FakeCustomCleanService
        {
            PreviewHandler = async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return CreatePreviewResult();
            }
        };
        var viewModel = CreateViewModel(scheduleService, cleanService);
        var selected = Assert.IsType<AutoCleanScheduleItemViewModel>(viewModel.SelectedSchedule);

        var runTask = viewModel.RunSelectedScheduleCommand.ExecuteAsync(selected);
        Assert.True(viewModel.IsBusy);
        viewModel.CancelRunCommand.Execute(null);
        await runTask;

        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.IsRunPreviewFresh);
        Assert.Empty(viewModel.RunResults);
        Assert.Contains("cancelled", viewModel.Status);
        Assert.Equal(0, cleanService.CleanCallCount);
    }

    [Fact]
    public async Task RunNow_WhenScanHasIssues_ShowsFirstIssueWithoutAutoSelection()
    {
        var scheduleService = new FakeScheduleService(CreateSchedule());
        var cleanService = new FakeCustomCleanService
        {
            PreviewResult = new CustomCleanPreviewResult(
                [CreatePreviewItem()],
                ["One temporary location was inaccessible."],
                TimeSpan.FromMilliseconds(20))
        };
        var viewModel = CreateViewModel(scheduleService, cleanService);

        await RunPreviewAsync(viewModel);

        Assert.Equal("1", viewModel.RunIssueCount);
        Assert.True(viewModel.HasRunFirstIssue);
        Assert.Equal(
            "One temporary location was inaccessible.",
            viewModel.RunFirstIssue);
        Assert.False(viewModel.RunResults[0].IsSelected);
    }

    private static async Task RunPreviewAsync(
        AutoCleanScheduleViewModel viewModel)
    {
        var selected = Assert.IsType<AutoCleanScheduleItemViewModel>(
            viewModel.SelectedSchedule);
        await viewModel.RunSelectedScheduleCommand.ExecuteAsync(selected);
    }

    private static AutoCleanScheduleViewModel CreateViewModel(
        FakeScheduleService scheduleService,
        FakeCustomCleanService cleanService,
        IAutoCleanRunConfirmationService? confirmationService = null,
        Func<DateTime>? nowProvider = null) =>
        new(
            scheduleService,
            cleanService,
            new AllowFeatureAccessGuard(),
            confirmationService ?? new FakeRunConfirmationService(true),
            nowProvider ?? (() => new DateTime(2026, 7, 31, 14, 30, 0)));

    private static AutoCleanSchedule CreateSchedule(bool isEnabled = true) =>
        new(
            Guid.NewGuid(),
            "Temporary files plan",
            isEnabled,
            AutoCleanScheduleFrequency.Daily,
            new TimeOnly(9, 0),
            DayOfWeek.Monday,
            1,
            [CustomCleanCategory.TemporaryFiles]);

    private static CustomCleanPreviewResult CreatePreviewResult() =>
        new(
            [CreatePreviewItem()],
            [],
            TimeSpan.FromMilliseconds(20));

    private static CustomCleanPreviewItem CreatePreviewItem() =>
        new(
            CustomCleanCategory.TemporaryFiles,
            Path.Combine(Path.GetTempPath(), $"spa-manual-run-{Guid.NewGuid():N}.tmp"),
            2048,
            new DateTime(2026, 7, 31, 8, 0, 0, DateTimeKind.Utc));

    private sealed class FakeScheduleService : IAutoCleanScheduleService
    {
        public FakeScheduleService(params AutoCleanSchedule[] schedules)
        {
            Schedules = schedules.ToList();
        }

        public List<AutoCleanSchedule> Schedules { get; private set; }

        public string SchedulesPath => "C:\\Test\\auto-clean-schedules.json";

        public AutoCleanScheduleLoadResult Load() =>
            new(Schedules.ToArray(), string.Empty);

        public void Save(IReadOnlyCollection<AutoCleanSchedule> schedules)
        {
            Schedules = schedules.ToList();
        }

        public DateTime? CalculateNextRun(
            AutoCleanSchedule schedule,
            DateTime localNow) =>
            schedule.IsEnabled
                ? localNow.AddHours(1)
                : null;
    }

    private sealed class FakeCustomCleanService : ICustomCleanService
    {
        public CustomCleanPreviewResult PreviewResult { get; init; } =
            new([], [], TimeSpan.Zero);

        public CustomCleanExecutionResult CleanResult { get; init; } =
            new(0, 0, 0, 0, 0, [], TimeSpan.Zero);

        public Func<CancellationToken, Task<CustomCleanPreviewResult>>?
            PreviewHandler { get; init; }

        public int PreviewCallCount { get; private set; }
        public int CleanCallCount { get; private set; }

        public Task<CustomCleanPreviewResult> PreviewAsync(
            IReadOnlyCollection<CustomCleanCategory> categories,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            PreviewCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(100);
            return PreviewHandler?.Invoke(cancellationToken) ??
                Task.FromResult(PreviewResult);
        }

        public Task<CustomCleanExecutionResult> CleanAsync(
            IReadOnlyCollection<CustomCleanCategory> categories,
            IReadOnlyCollection<CustomCleanPreviewItem> items,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CleanCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(100);
            return Task.FromResult(CleanResult);
        }
    }

    private sealed class FakeRunConfirmationService(bool confirmed) :
        IAutoCleanRunConfirmationService
    {
        public bool ConfirmCleanup(
            AutoCleanSchedule schedule,
            int selectedFileCount,
            long selectedBytes) => confirmed;
    }

    private sealed class AllowFeatureAccessGuard : IFeatureAccessGuard
    {
        public ApplicationEdition EffectiveEdition =>
            ApplicationEdition.Business;

        public bool IsDevelopmentOverrideActive => false;

        public FeatureAccessResult GetAccess(ApplicationFeature feature) =>
            new(
                feature,
                EffectiveEdition,
                FeatureAccessState.Available,
                null,
                "Available");

        public bool CanAccess(
            ApplicationFeature feature,
            FeatureAccessRequirement requirement) => true;
    }
}
