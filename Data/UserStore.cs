using UserManagementAPI.Models;

namespace UserManagementAPI.Data;

public static class UserStore
{
    public static List<User> Users { get; } = new();
}
