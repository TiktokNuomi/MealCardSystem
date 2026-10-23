namespace MealCardSystem.Model
{
    /// <summary>系统操作员。密码以 SHA256(salt + 密码) 存储，不保存明文。</summary>
    public class Operator
    {
        public long Id { get; set; }

        public string Username { get; set; }

        public string DisplayName { get; set; }

        /// <summary>是否管理员（管理员可管理其它操作员）。</summary>
        public bool IsAdmin { get; set; }

        /// <summary>界面显示名：有姓名用姓名，否则用账号。</summary>
        public string Caption
        {
            get
            {
                if (DisplayName != null && DisplayName.Length > 0)
                {
                    return DisplayName;
                }
                return Username;
            }
        }
    }
}
