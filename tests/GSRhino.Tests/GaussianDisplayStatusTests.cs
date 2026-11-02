using GSRhino.Core;
namespace GSRhino.Tests;

public sealed class GaussianDisplayStatusTests
{
    private static GaussianDisplayFacts Complete() => new()
    {
        DocumentInstances = 1, LoadedResources = 1, VisibleInstances = 1, ReadyPlacements = 1,
        HasFrame = true, FrameComplete = true, InputCount = 212864, ProjectedCount = 123456
    };

    [Fact]
    public void UnrecognizedDocumentNeverReportsReadyEvenWithStaleCompleteFrame()
    {
        Assert.Equal(GaussianDisplayPhase.NoRecognizedInstances, GaussianDisplayStatus.Evaluate(Complete() with
        { DocumentInstances = 0, VisibleInstances = 0, ReadyPlacements = 0 }));
        Assert.Equal(GaussianDisplayPhase.NoRecognizedInstances, GaussianDisplayStatus.Evaluate(new() { HasFrame = true, FrameComplete = true }));
    }

    [Fact]
    public void RecognizedReferencesWaitingForFirstLoadAreNotAnEmptySuccess()
    {
        Assert.Equal(GaussianDisplayPhase.WaitingForResources, GaussianDisplayStatus.Evaluate(Complete() with
        { LoadedResources = 0, ReadyPlacements = 0, InputCount = 0, ProjectedCount = 0 }));
    }

    [Fact]
    public void PendingAndFailedResourcesHaveDistinctStates()
    {
        var unavailable = Complete() with { LoadedResources = 0, ReadyPlacements = 0 };
        Assert.Equal(GaussianDisplayPhase.LoadingResources, GaussianDisplayStatus.Evaluate(unavailable with { LoadingResources = 1 }));
        Assert.Equal(GaussianDisplayPhase.ResourceLoadFailed, GaussianDisplayStatus.Evaluate(unavailable with { ResourceErrors = 1 }));
        Assert.Equal(GaussianDisplayPhase.LoadingResources, GaussianDisplayStatus.Evaluate(unavailable with { LoadingResources = 1, ResourceErrors = 1 }));
    }

    [Fact]
    public void HiddenOrOtherViewInstancesDoNotReuseStaleSuccessOrUnrelatedResourceFailure()
    {
        Assert.Equal(GaussianDisplayPhase.NoVisibleInstances, GaussianDisplayStatus.Evaluate(Complete() with
        { VisibleInstances = 0, ReadyPlacements = 0, ResourceErrors = 1 }));
    }

    [Fact]
    public void ALayoutOrMissingViewIsNotMisreportedAsHiddenGeometry()
    {
        Assert.Equal(GaussianDisplayPhase.WaitingForModelView, GaussianDisplayStatus.Evaluate(Complete() with
        { IsModelView = false, VisibleInstances = 0, ReadyPlacements = 0 }));
    }

    [Fact]
    public void LoadedAssetsWithoutUsableInstanceTransformsAreNotReady()
    {
        Assert.Equal(GaussianDisplayPhase.NoRenderablePlacements, GaussianDisplayStatus.Evaluate(Complete() with { ReadyPlacements = 0 }));
    }

    [Fact]
    public void CurrentModeAndCameraMustBeUsableBeforeReportingReady()
    {
        Assert.Equal(GaussianDisplayPhase.WaitingForGsMode, GaussianDisplayStatus.Evaluate(Complete() with { IsGsMode = false }));
        Assert.Equal(GaussianDisplayPhase.WaitingForStableView, GaussianDisplayStatus.Evaluate(Complete() with { HasCandidate = true }));
        Assert.Equal(GaussianDisplayPhase.WaitingForFrame, GaussianDisplayStatus.Evaluate(Complete() with { HasFrame = false }));
    }

    [Fact]
    public void CompletedZeroInputFrameNeverReportsReady()
    {
        Assert.Equal(GaussianDisplayPhase.WaitingForFrame, GaussianDisplayStatus.Evaluate(Complete() with { InputCount = 0, ProjectedCount = 0 }));
    }

    [Fact]
    public void CompletedPositiveInputWithZeroProjectionIsDistinguishedFromMissingAssets()
    {
        Assert.Equal(GaussianDisplayPhase.NoProjectedGaussians, GaussianDisplayStatus.Evaluate(Complete() with { ProjectedCount = 0 }));
    }

    [Fact]
    public void ActivePreparationAndUploadRemainDistinct()
    {
        Assert.Equal(GaussianDisplayPhase.Preparing, GaussianDisplayStatus.Evaluate(Complete() with { FrameComplete = false }));
        Assert.Equal(GaussianDisplayPhase.Uploading, GaussianDisplayStatus.Evaluate(Complete() with { FrameComplete = false, UploadPending = true }));
        Assert.Equal(GaussianDisplayPhase.PreparationFailed, GaussianDisplayStatus.Evaluate(Complete() with { FrameFailed = true }));
    }

    [Fact]
    public void SharedResourceCanBackMultipleReadyInstances()
    {
        Assert.Equal(GaussianDisplayPhase.Ready, GaussianDisplayStatus.Evaluate(Complete() with
        { DocumentInstances = 8, VisibleInstances = 8, ReadyPlacements = 8, LoadedResources = 1 }));
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(1, 0)] [InlineData(0, 1)]
    public void PartiallyLoadedVisibleSceneDoesNotClaimFullReadiness(int loading, int errors)
    {
        Assert.Equal(GaussianDisplayPhase.PartiallyReady, GaussianDisplayStatus.Evaluate(Complete() with
        { DocumentInstances = 3, VisibleInstances = 2, ReadyPlacements = 1, LoadingResources = loading, ResourceErrors = errors }));
    }

    [Fact]
    public void HiddenResourceErrorsDoNotPreventReadyVisibleInstances()
    {
        Assert.Equal(GaussianDisplayPhase.Ready, GaussianDisplayStatus.Evaluate(Complete() with
        { DocumentInstances = 2, ResourceErrors = 1 }));
    }
}
