using GobanSource.Bus.Redis;
using Moq;

namespace GobanSource.ReplicatedLruCache.Tests.UnitTests;

[TestClass]
public class ReplicatedLruCacheTests
{
    private Mock<ILruCache> _mockLocalCache = null!;
    private Mock<IRedisSyncBus<CacheMessage>> _mockSyncBus = null!;
    private ReplicatedLruCache _lruCache = null!;
    private const string CacheInstanceId = "test-cache";
    private string _appId = null!;

    [TestInitialize]
    public void Setup()
    {
        _appId = Guid.NewGuid().ToString();
        _mockLocalCache = new Mock<ILruCache>();
        _mockSyncBus = new Mock<IRedisSyncBus<CacheMessage>>();
        _lruCache = new ReplicatedLruCache(_mockLocalCache.Object, _mockSyncBus.Object, CacheInstanceId);
    }

    [TestMethod]
    public async Task Set_ShouldUpdateLocalCacheAndPublishMessage()
    {
        // Arrange
        var key = "key1";
        var value = "value1";
        var ttl = TimeSpan.FromMinutes(5);

        // Act
        await _lruCache.Set(key, value, ttl);

        // Assert
        _mockLocalCache.Verify(c => c.Set(key, value, ttl), Times.Once);
        _mockSyncBus.Verify(s => s.PublishAsync(It.Is<CacheMessage>(m =>

            m.CacheInstanceId == CacheInstanceId &&
            m.Operation == CacheOperation.Set &&
            m.Key == key &&
            m.Value == value &&
            m.TTL == ttl)), Times.Once);
    }

    [TestMethod]
    public async Task Set_WithoutTTL_ShouldUpdateLocalCacheAndPublishMessage()
    {
        // Arrange
        var key = "key1";
        var value = "value1";

        // Act
        await _lruCache.Set(key, value);

        // Assert
        _mockLocalCache.Verify(c => c.Set(key, value, null), Times.Once);
        _mockSyncBus.Verify(s => s.PublishAsync(It.Is<CacheMessage>(m =>

            m.CacheInstanceId == CacheInstanceId &&
            m.Operation == CacheOperation.Set &&
            m.Key == key &&
            m.Value == value &&
            m.TTL == null)), Times.Once);
    }

    [TestMethod]
    public void TryGet_ShouldReturnValueFromLocalCache()
    {
        // Arrange
        var key = "key1";
        var expectedValue = "value1";
        _mockLocalCache.Setup(c => c.TryGet(key, out expectedValue)).Returns(true);

        // Act
        var result = _lruCache.TryGet(key, out var actualValue);

        // Assert
        Assert.IsTrue(result);
        Assert.AreEqual(expectedValue, actualValue);
        _mockLocalCache.Verify(c => c.TryGet(key, out expectedValue), Times.Once);
    }

    [TestMethod]
    public void TryGet_WhenKeyNotFound_ShouldReturnFalse()
    {
        // Arrange
        var key = "nonexistent";
        string? outValue = null;
        _mockLocalCache.Setup(c => c.TryGet(key, out outValue)).Returns(false);

        // Act
        var result = _lruCache.TryGet(key, out var value);

        // Assert
        Assert.IsFalse(result);
        Assert.IsNull(value);
        _mockLocalCache.Verify(c => c.TryGet(key, out outValue), Times.Once);
    }

    [TestMethod]
    public async Task Remove_ShouldRemoveFromLocalCacheAndPublishMessage()
    {
        // Arrange
        var key = "key1";

        // Act
        await _lruCache.Remove(key);

        // Assert
        _mockLocalCache.Verify(c => c.Remove(key), Times.Once);
        _mockSyncBus.Verify(s => s.PublishAsync(It.Is<CacheMessage>(m =>

            m.CacheInstanceId == CacheInstanceId &&
            m.Operation == CacheOperation.Remove &&
            m.Key == key)), Times.Once);
    }

    [TestMethod]
    public async Task Clear_ShouldClearLocalCacheAndPublishMessage()
    {
        // Act
        await _lruCache.Clear();

        // Assert
        _mockLocalCache.Verify(c => c.Clear(), Times.Once);
        _mockSyncBus.Verify(s => s.PublishAsync(It.Is<CacheMessage>(m =>

            m.CacheInstanceId == CacheInstanceId &&
            m.Operation == CacheOperation.Clear)), Times.Once);
    }

    [TestMethod]
    public async Task Set_WhenPublishFails_ShouldThrowException()
    {
        // Arrange
        var key = "key1";
        var value = "value1";
        _mockSyncBus.Setup(s => s.PublishAsync(It.IsAny<CacheMessage>()))
            .ThrowsAsync(new Exception("Publish failed"));

        // Act & Assert
        await Assert.ThrowsExceptionAsync<Exception>(() => _lruCache.Set(key, value));
        _mockLocalCache.Verify(c => c.Set(key, value, null), Times.Once);
    }

    [TestMethod]
    public async Task Remove_WhenPublishFails_ShouldThrowException()
    {
        // Arrange
        var key = "key1";
        _mockSyncBus.Setup(s => s.PublishAsync(It.IsAny<CacheMessage>()))
            .ThrowsAsync(new Exception("Publish failed"));

        // Act & Assert
        await Assert.ThrowsExceptionAsync<Exception>(() => _lruCache.Remove(key));
        _mockLocalCache.Verify(c => c.Remove(key), Times.Once);
    }

    [TestMethod]
    public async Task Clear_WhenPublishFails_ShouldThrowException()
    {
        // Arrange
        _mockSyncBus.Setup(s => s.PublishAsync(It.IsAny<CacheMessage>()))
            .ThrowsAsync(new Exception("Publish failed"));

        // Act & Assert
        await Assert.ThrowsExceptionAsync<Exception>(() => _lruCache.Clear());
        _mockLocalCache.Verify(c => c.Clear(), Times.Once);
    }
}