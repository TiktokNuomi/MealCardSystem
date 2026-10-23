using System;

namespace MealCardSystem.Service
{
    /// <summary>
    /// 业务规则导致的失败（余额不足、卡已挂失、卡号重复……）。
    /// 这类异常的 Message 是可以直接展示给操作员的中文说明，界面捕获后弹提示即可，
    /// 与真正的程序故障（数据库损坏、串口异常）区分开。
    /// </summary>
    public class BusinessException : Exception
    {
        public BusinessException(string message) : base(message)
        {
        }
    }
}
