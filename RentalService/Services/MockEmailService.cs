using RentalService.Models;

namespace RentalService.Services;

public class MockEmailService
{
    public void SendRegistrationEmail(string email, string fullName)
    {
        Console.WriteLine($"[MockEmailService] Письмо о регистрации на {email}: Добро пожаловать, {fullName}. Ваш аккаунт зарегистрирован и верифицирован.");
    }

    public void SendBookingCreatedEmail(Booking booking)
    {
        Console.WriteLine($"[MockEmailService] Письмо арендодателю: получен новый запрос на бронирование {booking.Id}. Даты: {booking.CheckIn} — {booking.CheckOut}.");
    }

    public void SendBookingConfirmedEmail(Booking booking)
    {
        Console.WriteLine($"[MockEmailService] Письмо об оплате: бронирование {booking.Id} успешно оплачено. Сумма: {booking.TotalPrice} руб.");
    }

    public void SendBookingCancelledEmail(Booking booking)
    {
        Console.WriteLine($"[MockEmailService] Письмо об отмене: бронирование {booking.Id} отменено. Сумма возврата: {booking.TotalPrice} руб.");
    }

    public void SendBookingRescheduledEmail(Booking booking)
    {
        Console.WriteLine($"[MockEmailService] Письмо о переносе: бронирование {booking.Id} перенесено на новые даты {booking.CheckIn} — {booking.CheckOut}. Новая сумма: {booking.TotalPrice} руб.");
    }
}
