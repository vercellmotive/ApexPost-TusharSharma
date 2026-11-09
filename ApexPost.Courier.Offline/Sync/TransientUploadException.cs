namespace ApexPost.Courier.Offline.Sync;

/// <summary>
/// An uploader throws this when the upload could not happen for a reason that may pass, such as no network or a
/// server that is down. The outbox leaves the scan Pending to try again, instead of marking it Failed for good.
/// </summary>
public sealed class TransientUploadException(string message, Exception? innerException = null) : Exception(message, innerException);
