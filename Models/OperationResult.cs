using System;

namespace ViaManager.Models
{
    public class OperationResult
    {
        public bool IsSuccess { get; }
        public string Message { get; }
        public string ErrorCode { get; }

        protected OperationResult(bool isSuccess, string message, string errorCode = "")
        {
            IsSuccess = isSuccess;
            Message = message;
            ErrorCode = errorCode;
        }

        public static OperationResult Ok(string message = "") => new(true, message);
        public static OperationResult Fail(string message, string errorCode = "") => new(false, message, errorCode);
    }

    public class OperationResult<T> : OperationResult
    {
        public T? Data { get; }

        private OperationResult(bool isSuccess, T? data, string message, string errorCode = "")
            : base(isSuccess, message, errorCode)
        {
            Data = data;
        }

        public static OperationResult<T> Ok(T data, string message = "") => new(true, data, message);
        public static new OperationResult<T> Fail(string message, string errorCode = "") => new(false, default, message, errorCode);
    }
}
