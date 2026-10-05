--
-- PostgreSQL database dump
--

\restrict wnn44KOcLifb0JhuHn5Yeyd96Y5vGCIbkKoap4fr5Pzqk20GQ8W38rI0smdDbes

-- Dumped from database version 18.4
-- Dumped by pg_dump version 18.4

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: __EFMigrationsHistory; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL
);


--
-- Name: app_settings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.app_settings (
    "Id" uuid NOT NULL,
    "BranchId" uuid,
    "Key" character varying(200) NOT NULL,
    "Value" text NOT NULL,
    "Description" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: audit_logs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.audit_logs (
    "Id" uuid NOT NULL,
    "BranchId" uuid,
    "EmployeeId" uuid,
    "Action" character varying(100) NOT NULL,
    "EntityType" character varying(100) NOT NULL,
    "EntityId" character varying(100),
    "DetailsJson" text,
    "IpAddress" character varying(64),
    "UserAgent" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: bar_order_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.bar_order_items (
    "Id" uuid NOT NULL,
    "BarOrderId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "ProductName" character varying(200) NOT NULL,
    "Quantity" numeric(18,3) NOT NULL,
    "UnitPrice" numeric(18,2) NOT NULL,
    "LineTotal" numeric(18,2) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: bar_orders; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.bar_orders (
    "Id" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "Number" character varying(40) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "PaymentMode" character varying(32) NOT NULL,
    "ComputerId" uuid,
    "GamingSessionId" uuid,
    "CustomerId" uuid,
    "AssignedEmployeeId" uuid,
    "CreatedByEmployeeId" uuid,
    "Total" numeric(18,2) NOT NULL,
    "Comment" character varying(500),
    "IdempotencyKey" character varying(100),
    "AcceptedAt" timestamp with time zone,
    "ReadyAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    "ReceiptId" uuid,
    "StockDeducted" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: booking_computers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.booking_computers (
    "Id" uuid NOT NULL,
    "BookingId" uuid NOT NULL,
    "ComputerId" uuid NOT NULL,
    "GamingSessionId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: bookings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.bookings (
    "Id" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "ZoneId" uuid,
    "CustomerId" uuid,
    "ContactName" character varying(200) NOT NULL,
    "ContactPhone" character varying(32) NOT NULL,
    "Comment" character varying(1000),
    "Status" character varying(32) NOT NULL,
    "StartsAt" timestamp with time zone NOT NULL,
    "EndsAt" timestamp with time zone NOT NULL,
    "DurationMinutes" integer NOT NULL,
    "PrepaidAmount" numeric(18,2) NOT NULL,
    "TotalEstimated" numeric(18,2) NOT NULL,
    "PrepayMethod" character varying(32),
    "PrepayReceiptId" uuid,
    "GraceMinutes" integer NOT NULL,
    "CreatedByEmployeeId" uuid NOT NULL,
    "CancelledByEmployeeId" uuid,
    "CancelReason" character varying(500),
    "CancelledAt" timestamp with time zone,
    "ArrivedAt" timestamp with time zone,
    "NoShowAt" timestamp with time zone,
    "IdempotencyKey" character varying(100),
    "Number" character varying(40) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: branches; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.branches (
    "Id" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Code" character varying(50) NOT NULL,
    "TimeZoneId" character varying(100) NOT NULL,
    "CurrencyCode" character varying(3) NOT NULL,
    "Address" character varying(500),
    "Phone" character varying(50),
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid,
    "FloorGridCols" integer DEFAULT 24 NOT NULL,
    "FloorGridRows" integer DEFAULT 16 NOT NULL,
    "FloorBackgroundHex" character varying(16)
);


--
-- Name: case_definitions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.case_definitions (
    "Id" uuid NOT NULL,
    "Code" character varying(64) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Description" character varying(1000),
    "EconomicsNote" character varying(1000),
    "ExpectedCostKzt" numeric(18,2) NOT NULL,
    "LimitsJson" text,
    "ShowProbabilitiesToUsers" boolean NOT NULL,
    "IsEnabled" boolean NOT NULL,
    "KeyCost" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: case_key_ledger; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.case_key_ledger (
    "Id" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "Delta" integer NOT NULL,
    "BalanceAfter" integer NOT NULL,
    "Reason" integer NOT NULL,
    "IdempotencyKey" character varying(200),
    "Comment" character varying(500),
    "EmployeeId" uuid,
    "RelatedEntityId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: case_openings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.case_openings (
    "Id" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "CaseDefinitionId" uuid NOT NULL,
    "CasePrizeId" uuid NOT NULL,
    "PrizeCodeSnapshot" character varying(64) NOT NULL,
    "PrizeNameSnapshot" character varying(200) NOT NULL,
    "RaritySnapshot" integer NOT NULL,
    "PrizeTypeSnapshot" integer NOT NULL,
    "PayloadJsonSnapshot" text NOT NULL,
    "ImageUrlSnapshot" character varying(500),
    "KeyCost" integer NOT NULL,
    "WeightRoll" integer NOT NULL,
    "WeightTotal" integer NOT NULL,
    "ClubDayKey" character varying(16) NOT NULL,
    "IdempotencyKey" character varying(200),
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: case_prizes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.case_prizes (
    "Id" uuid NOT NULL,
    "CaseDefinitionId" uuid NOT NULL,
    "PrizeCode" character varying(64) NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Description" character varying(1000),
    "PrizeType" integer NOT NULL,
    "Rarity" integer NOT NULL,
    "Weight" integer NOT NULL,
    "CostEstimateKzt" numeric(18,2) NOT NULL,
    "PayloadJson" text NOT NULL,
    "ImageUrl" character varying(500),
    "DailyLimit" integer,
    "TotalLimit" integer,
    "RequiresClaim" boolean NOT NULL,
    "SortOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: case_user_rewards; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.case_user_rewards (
    "Id" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "CaseOpeningId" uuid NOT NULL,
    "CasePrizeId" uuid NOT NULL,
    "PrizeCode" character varying(64) NOT NULL,
    "Name" character varying(200) NOT NULL,
    "PrizeType" integer NOT NULL,
    "PayloadJson" text NOT NULL,
    "ImageUrl" character varying(500),
    "Status" integer NOT NULL,
    "AppliedAt" timestamp with time zone,
    "ClaimedAt" timestamp with time zone,
    "ClaimedByEmployeeId" uuid,
    "ClaimNote" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: cash_movements; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cash_movements (
    "Id" uuid NOT NULL,
    "CashShiftId" uuid NOT NULL,
    "Type" character varying(32) NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "EmployeeId" uuid NOT NULL,
    "Category" character varying(100),
    "Comment" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: cash_registers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cash_registers (
    "Id" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Code" character varying(50) NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: cash_shifts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cash_shifts (
    "Id" uuid NOT NULL,
    "CashRegisterId" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "Number" character varying(40) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "OpenedByEmployeeId" uuid NOT NULL,
    "ClosedByEmployeeId" uuid,
    "OpenedAt" timestamp with time zone NOT NULL,
    "ClosedAt" timestamp with time zone,
    "OpeningCash" numeric(18,2) NOT NULL,
    "ClosingCashActual" numeric(18,2),
    "ClosingCashExpected" numeric(18,2),
    "Discrepancy" numeric(18,2),
    "DiscrepancyReason" character varying(500),
    "OpenComment" character varying(500),
    "CloseComment" character varying(500),
    "SalesCash" numeric(18,2) NOT NULL,
    "SalesCard" numeric(18,2) NOT NULL,
    "SalesOther" numeric(18,2) NOT NULL,
    "RefundsCash" numeric(18,2) NOT NULL,
    "CashInTotal" numeric(18,2) NOT NULL,
    "CashOutTotal" numeric(18,2) NOT NULL,
    "ExpenseTotal" numeric(18,2) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid,
    "SalesKaspi" numeric(18,2) DEFAULT 0.0 NOT NULL,
    "SalesTransfer" numeric(18,2) DEFAULT 0.0 NOT NULL,
    "RefundsCard" numeric(18,2) DEFAULT 0.0 NOT NULL,
    "RefundsKaspi" numeric(18,2) DEFAULT 0.0 NOT NULL,
    "RefundsTransfer" numeric(18,2) DEFAULT 0.0 NOT NULL,
    "RefundsOther" numeric(18,2) DEFAULT 0.0 NOT NULL,
    "DepositsTotal" numeric(18,2) DEFAULT 0.0 NOT NULL
);


--
-- Name: club_news_posts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.club_news_posts (
    "Id" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Body" character varying(4000) NOT NULL,
    "ImageUrl" character varying(1000),
    "Category" character varying(40) NOT NULL,
    "IsPublished" boolean NOT NULL,
    "IsPinned" boolean NOT NULL,
    "SortOrder" integer NOT NULL,
    "PublishAt" timestamp with time zone,
    "ExpireAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: computer_commands; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.computer_commands (
    "Id" uuid NOT NULL,
    "ComputerId" uuid NOT NULL,
    "Type" character varying(40) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "InitiatedByEmployeeId" uuid,
    "PayloadJson" text,
    "IdempotencyKey" character varying(100),
    "SentAt" timestamp with time zone,
    "ReceivedAt" timestamp with time zone,
    "ExecutedAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    "ExpiresAt" timestamp with time zone,
    "ResultJson" text,
    "ErrorMessage" character varying(1000),
    "AttemptCount" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: computer_heartbeats; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.computer_heartbeats (
    "Id" uuid NOT NULL,
    "ComputerId" uuid NOT NULL,
    "ReceivedAt" timestamp with time zone NOT NULL,
    "CpuLoadPercent" double precision,
    "RamUsedPercent" double precision,
    "FreeDiskMb" bigint,
    "UptimeSeconds" bigint,
    "IpAddress" character varying(64),
    "ClientVersion" character varying(50),
    "StatusNote" character varying(500),
    "ShellRunning" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: computers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.computers (
    "Id" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "ZoneId" uuid,
    "DisplayName" character varying(200),
    "WindowsName" character varying(200) NOT NULL,
    "InstallationId" character varying(100) NOT NULL,
    "IpAddress" character varying(64),
    "MacAddress" character varying(64),
    "MapX" double precision,
    "MapY" double precision,
    "ClientVersion" character varying(50),
    "WindowsVersion" character varying(100),
    "CpuName" character varying(200),
    "GpuName" character varying(200),
    "RamMb" integer,
    "ScreenResolution" character varying(50),
    "Status" character varying(32) NOT NULL,
    "LastSeenAt" timestamp with time zone,
    "LastHeartbeatAt" timestamp with time zone,
    "IsApproved" boolean NOT NULL,
    "RegistrationCode" character varying(16),
    "RegistrationCodeExpiresAt" timestamp with time zone,
    "DeviceTokenHash" character varying(500),
    "IsMaintenance" boolean NOT NULL,
    "Notes" character varying(2000),
    "Tags" character varying(500),
    "LastDiagnosticsAt" timestamp with time zone,
    "RowVersion" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid,
    "CurrentSessionId" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "GridCol" integer,
    "GridRow" integer,
    "StationKind" character varying(16) DEFAULT 'Pc'::character varying NOT NULL,
    "GridColSpan" integer DEFAULT 1 NOT NULL,
    "GridRowSpan" integer DEFAULT 1 NOT NULL
);


--
-- Name: customer_balance_transactions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.customer_balance_transactions (
    "Id" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "Type" character varying(40) NOT NULL,
    "Direction" character varying(16) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "BalanceBefore" numeric(18,2) NOT NULL,
    "BalanceAfter" numeric(18,2) NOT NULL,
    "EmployeeId" uuid,
    "ReceiptId" uuid,
    "GamingSessionId" uuid,
    "SourceType" character varying(100),
    "SourceId" character varying(100),
    "IdempotencyKey" character varying(100),
    "Comment" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: customer_packages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.customer_packages (
    "Id" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "TotalMinutes" integer NOT NULL,
    "RemainingMinutes" integer NOT NULL,
    "PaidAmount" numeric(18,2) NOT NULL,
    "PurchasedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: customer_telegram_outreach; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.customer_telegram_outreach (
    "Id" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "Kind" character varying(64) NOT NULL,
    "SentAt" timestamp with time zone NOT NULL,
    "Preview" character varying(240),
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: customer_time_bank_transactions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.customer_time_bank_transactions (
    "Id" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "Reason" character varying(40) NOT NULL,
    "Direction" character varying(16) NOT NULL,
    "Minutes" integer NOT NULL,
    "BalanceBefore" integer NOT NULL,
    "BalanceAfter" integer NOT NULL,
    "EmployeeId" uuid,
    "GamingSessionId" uuid,
    "IdempotencyKey" character varying(100),
    "Comment" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid,
    "ZoneId" uuid NOT NULL
);


--
-- Name: customer_zone_time_banks; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.customer_zone_time_banks (
    "Id" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "ZoneId" uuid NOT NULL,
    "Minutes" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: customers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.customers (
    "Id" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "FirstName" character varying(100) NOT NULL,
    "LastName" character varying(100) NOT NULL,
    "Phone" character varying(32) NOT NULL,
    "Email" character varying(200),
    "BirthDate" date,
    "Login" character varying(100),
    "PasswordHash" character varying(500),
    "PinHash" character varying(500),
    "Balance" numeric(18,2) NOT NULL,
    "BonusBalance" numeric(18,2) NOT NULL,
    "LoyaltyLevelId" uuid,
    "VisitCount" integer NOT NULL,
    "TotalMinutesPlayed" integer NOT NULL,
    "TotalSpent" numeric(18,2) NOT NULL,
    "Notes" character varying(1000),
    "IsBlocked" boolean NOT NULL,
    "BlockReason" character varying(500),
    "AllowNotifications" boolean NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid,
    "TimeBankMinutes" integer DEFAULT 0 NOT NULL,
    "LoggedInComputerId" uuid,
    "LoggedInAt" timestamp with time zone,
    "LoginEpoch" integer DEFAULT 0 NOT NULL,
    "LoyaltyLevelLocked" boolean DEFAULT false NOT NULL,
    "TelegramUserId" bigint,
    "TelegramLinkedAt" timestamp with time zone,
    "VisitStreakDays" integer DEFAULT 0 NOT NULL,
    "VisitStreakLastDate" date,
    "BirthdayGiftYear" integer,
    "PendingBarRewards" integer DEFAULT 0 NOT NULL,
    "ComfortHideBalance" boolean DEFAULT false NOT NULL,
    "ComfortSoundEnabled" boolean DEFAULT true NOT NULL,
    "ComfortLanguage" character varying(8) DEFAULT 'ru'::character varying NOT NULL,
    "ComfortBrightness" integer DEFAULT 100 NOT NULL,
    "TelegramChangedAt" timestamp with time zone,
    "Iin" character varying(12),
    "CaseKeysBalance" integer DEFAULT 0 NOT NULL,
    "CasePlayBaselineMinutes" integer DEFAULT 0 NOT NULL
);


--
-- Name: document_sequences; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.document_sequences (
    "Id" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "Type" character varying(32) NOT NULL,
    "Year" integer NOT NULL,
    "LastValue" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: employee_credentials; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.employee_credentials (
    "Id" uuid NOT NULL,
    "EmployeeId" uuid NOT NULL,
    "PasswordHash" character varying(500) NOT NULL,
    "PinHash" character varying(500),
    "AccessFailedCount" integer NOT NULL,
    "LockoutEnd" timestamp with time zone,
    "PasswordChangedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: employee_roles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.employee_roles (
    "EmployeeId" uuid NOT NULL,
    "RoleId" uuid NOT NULL
);


--
-- Name: employees; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.employees (
    "Id" uuid NOT NULL,
    "BranchId" uuid,
    "Login" character varying(100) NOT NULL,
    "DisplayName" character varying(200) NOT NULL,
    "Email" character varying(200),
    "Phone" character varying(50),
    "IsActive" boolean NOT NULL,
    "LastLoginAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid,
    "HourlyRate" numeric(18,2) DEFAULT 0.0 NOT NULL,
    "MonthlySalary" numeric(18,2) DEFAULT 0.0 NOT NULL,
    "PayType" character varying(32) DEFAULT ''::character varying NOT NULL,
    "ShiftRate" numeric(18,2) DEFAULT 0.0 NOT NULL,
    "FirstName" character varying(100) DEFAULT ''::character varying NOT NULL,
    "LastName" character varying(100) DEFAULT ''::character varying NOT NULL,
    "Iin" character varying(12),
    "TelegramUserId" bigint,
    "TelegramLinkedAt" timestamp with time zone
);


--
-- Name: floor_map_elements; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.floor_map_elements (
    "Id" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "Kind" character varying(32) NOT NULL,
    "Label" character varying(500),
    "GridCol" integer NOT NULL,
    "GridRow" integer NOT NULL,
    "ColSpan" integer NOT NULL,
    "RowSpan" integer NOT NULL,
    "EndCol" integer,
    "EndRow" integer,
    "ColorHex" character varying(16),
    "SortOrder" integer NOT NULL,
    "IsVisible" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid,
    "FillHex" character varying(16),
    "StrokeWidth" integer,
    "FontSize" integer,
    "ShowIcon" boolean DEFAULT true NOT NULL,
    "RotationDeg" integer DEFAULT 0 NOT NULL
);


--
-- Name: gaming_sessions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.gaming_sessions (
    "Id" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "ComputerId" uuid NOT NULL,
    "ZoneId" uuid NOT NULL,
    "TariffId" uuid NOT NULL,
    "CustomerId" uuid,
    "GuestName" character varying(200),
    "PaymentMethod" character varying(32) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    "PlannedEndsAt" timestamp with time zone,
    "ActualEndedAt" timestamp with time zone,
    "DurationMinutes" integer NOT NULL,
    "BasePrice" numeric(18,2) NOT NULL,
    "DiscountAmount" numeric(18,2) NOT NULL,
    "TotalPrice" numeric(18,2) NOT NULL,
    "PaidAmount" numeric(18,2) NOT NULL,
    "DebtAmount" numeric(18,2) NOT NULL,
    "StartedByEmployeeId" uuid NOT NULL,
    "EndedByEmployeeId" uuid,
    "CancelReason" character varying(500),
    "IdempotencyKey" character varying(100),
    "Warning15Sent" boolean NOT NULL,
    "Warning10Sent" boolean NOT NULL,
    "Warning5Sent" boolean NOT NULL,
    "Warning1Sent" boolean NOT NULL,
    "RowVersion" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid,
    "PausedAt" timestamp with time zone,
    "RemainingSecondsAtPause" integer,
    "TotalPausedSeconds" integer DEFAULT 0 NOT NULL,
    "DurationMode" character varying(32) DEFAULT 'FixedDuration'::character varying NOT NULL,
    "WindowPeriodStartsAt" timestamp with time zone,
    "WindowPeriodEndsAt" timestamp with time zone,
    "IsComplimentary" boolean DEFAULT false NOT NULL,
    "PrepaidAppliedAmount" numeric(18,2) DEFAULT 0.0 NOT NULL
);


--
-- Name: inventory_movements; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.inventory_movements (
    "Id" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "Type" character varying(32) NOT NULL,
    "QuantityDelta" numeric(18,3) NOT NULL,
    "StockAfter" numeric(18,3) NOT NULL,
    "UnitCost" numeric(18,2),
    "EmployeeId" uuid,
    "ReferenceId" uuid,
    "Comment" character varying(500),
    "IdempotencyKey" character varying(100),
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: loyalty_levels; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.loyalty_levels (
    "Id" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "Name" character varying(100) NOT NULL,
    "Code" character varying(50) NOT NULL,
    "MinSpent" integer NOT NULL,
    "BonusPercent" numeric(18,2) NOT NULL,
    "SortOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid,
    "TimeDiscountPercent" numeric(18,2) DEFAULT 0.0 NOT NULL
);


--
-- Name: payments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payments (
    "Id" uuid NOT NULL,
    "ReceiptId" uuid NOT NULL,
    "Method" character varying(32) NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "ExternalReference" character varying(200),
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: payroll_accruals; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payroll_accruals (
    "Id" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "EmployeeId" uuid NOT NULL,
    "PeriodFrom" date NOT NULL,
    "PeriodTo" date NOT NULL,
    "Type" character varying(32) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Basis" character varying(300),
    "Comment" character varying(500),
    "CreatedByEmployeeId" uuid NOT NULL,
    "ApprovedByEmployeeId" uuid,
    "PaidAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: permissions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.permissions (
    "Id" uuid NOT NULL,
    "Code" character varying(100) NOT NULL,
    "Name" character varying(200) NOT NULL,
    "GroupName" character varying(100),
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: product_categories; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.product_categories (
    "Id" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Code" character varying(50) NOT NULL,
    "SortOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: products; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.products (
    "Id" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "CategoryId" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Sku" character varying(50) NOT NULL,
    "Barcode" character varying(64),
    "Unit" character varying(20) NOT NULL,
    "CostPrice" numeric(18,2) NOT NULL,
    "SalePrice" numeric(18,2) NOT NULL,
    "StockQty" numeric(18,3) NOT NULL,
    "MinStockQty" numeric(18,3) NOT NULL,
    "IsActive" boolean NOT NULL,
    "Notes" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid,
    "ImageUrl" character varying(500)
);


--
-- Name: receipt_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.receipt_items (
    "Id" uuid NOT NULL,
    "ReceiptId" uuid NOT NULL,
    "ItemType" character varying(32) NOT NULL,
    "Name" character varying(300) NOT NULL,
    "Quantity" numeric(18,3) NOT NULL,
    "UnitPrice" numeric(18,2) NOT NULL,
    "DiscountAmount" numeric(18,2) NOT NULL,
    "LineTotal" numeric(18,2) NOT NULL,
    "ReferenceId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: receipts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.receipts (
    "Id" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "CashShiftId" uuid NOT NULL,
    "Number" character varying(40) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "CustomerId" uuid,
    "ComputerId" uuid,
    "GamingSessionId" uuid,
    "CreatedByEmployeeId" uuid NOT NULL,
    "Subtotal" numeric(18,2) NOT NULL,
    "DiscountAmount" numeric(18,2) NOT NULL,
    "Total" numeric(18,2) NOT NULL,
    "PaidTotal" numeric(18,2) NOT NULL,
    "Comment" character varying(500),
    "IdempotencyKey" character varying(100),
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: role_permissions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.role_permissions (
    "RoleId" uuid NOT NULL,
    "PermissionId" uuid NOT NULL
);


--
-- Name: roles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.roles (
    "Id" uuid NOT NULL,
    "Name" character varying(100) NOT NULL,
    "Code" character varying(50) NOT NULL,
    "Description" character varying(500),
    "IsSystem" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: session_history; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.session_history (
    "Id" uuid NOT NULL,
    "SessionId" uuid NOT NULL,
    "Action" character varying(40) NOT NULL,
    "EmployeeId" uuid,
    "DetailsJson" text,
    "PriceBefore" numeric(18,2),
    "PriceAfter" numeric(18,2),
    "PlannedEndsAtBefore" timestamp with time zone,
    "PlannedEndsAtAfter" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: software_apps; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.software_apps (
    "Id" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Category" character varying(100) NOT NULL,
    "ExePath" character varying(1000) NOT NULL,
    "Arguments" character varying(500),
    "WorkingDirectory" character varying(1000),
    "IconPath" character varying(1000),
    "SortOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "MinAge" integer,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid,
    "LaunchSoundUrl" character varying(1000)
);


--
-- Name: staff_wiki_pages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.staff_wiki_pages (
    "Id" uuid NOT NULL,
    "Slug" character varying(80) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "BodyMarkdown" text NOT NULL,
    "SortOrder" integer NOT NULL,
    "UpdatedByEmployeeId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: tariffs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.tariffs (
    "Id" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "ZoneId" uuid,
    "Name" character varying(200) NOT NULL,
    "Code" character varying(50) NOT NULL,
    "Kind" character varying(32) NOT NULL,
    "BillingMode" character varying(32) NOT NULL,
    "PricePerHour" numeric(18,2) NOT NULL,
    "MinCharge" numeric(18,2) NOT NULL,
    "FixedDurationMinutes" integer,
    "FixedPrice" numeric(18,2),
    "IsActive" boolean NOT NULL,
    "SortOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid,
    "AllowPause" boolean DEFAULT false NOT NULL,
    "AvailableFrom" time without time zone,
    "AvailableTo" time without time zone,
    "ColorHex" character varying(16),
    "DaysOfWeekMask" integer DEFAULT 0 NOT NULL,
    "Description" character varying(500),
    "MaxDurationMinutes" integer,
    "MinDurationMinutes" integer,
    "DurationMode" character varying(32) DEFAULT 'FixedDuration'::character varying NOT NULL
);


--
-- Name: telegram_auth_tickets; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.telegram_auth_tickets (
    "Id" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "Code" character varying(16) NOT NULL,
    "Purpose" character varying(32) NOT NULL,
    "ComputerId" uuid,
    "SessionId" uuid,
    "CustomerId" uuid,
    "TelegramUserId" bigint,
    "Status" character varying(32) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "ConsumedAt" timestamp with time zone,
    "ResultMessage" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid,
    "PendingDisplayName" character varying(120),
    "ClientNonce" character varying(64),
    "AuthRedeemed" boolean DEFAULT false NOT NULL
);


--
-- Name: work_shift_notes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.work_shift_notes (
    "Id" uuid NOT NULL,
    "WorkShiftId" uuid NOT NULL,
    "AuthorEmployeeId" uuid NOT NULL,
    "AuthorName" character varying(200) NOT NULL,
    "Kind" character varying(32) NOT NULL,
    "Text" character varying(2000) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid
);


--
-- Name: work_shifts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.work_shifts (
    "Id" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "EmployeeId" uuid NOT NULL,
    "WorkDate" date NOT NULL,
    "PlannedStart" time without time zone NOT NULL,
    "PlannedEnd" time without time zone NOT NULL,
    "Status" character varying(32) NOT NULL,
    "ActualStartAt" timestamp with time zone,
    "ActualEndAt" timestamp with time zone,
    "BreakMinutes" integer NOT NULL,
    "Comment" character varying(500),
    "CreatedByEmployeeId" uuid,
    "SubstituteEmployeeId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid,
    "BreakStartedAt" timestamp with time zone
);


--
-- Name: zones; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.zones (
    "Id" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Code" character varying(50) NOT NULL,
    "ColorHex" character varying(16) NOT NULL,
    "SortOrder" integer NOT NULL,
    "MinSessionMinutes" integer,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    "UpdatedBy" uuid,
    "Kind" character varying(32) DEFAULT 'Hall'::character varying NOT NULL,
    "GridColumns" integer DEFAULT 6 NOT NULL,
    "GridRows" integer DEFAULT 4 NOT NULL
);


--
-- Name: __EFMigrationsHistory PK___EFMigrationsHistory; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."__EFMigrationsHistory"
    ADD CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId");


--
-- Name: app_settings PK_app_settings; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_settings
    ADD CONSTRAINT "PK_app_settings" PRIMARY KEY ("Id");


--
-- Name: audit_logs PK_audit_logs; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.audit_logs
    ADD CONSTRAINT "PK_audit_logs" PRIMARY KEY ("Id");


--
-- Name: bar_order_items PK_bar_order_items; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bar_order_items
    ADD CONSTRAINT "PK_bar_order_items" PRIMARY KEY ("Id");


--
-- Name: bar_orders PK_bar_orders; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bar_orders
    ADD CONSTRAINT "PK_bar_orders" PRIMARY KEY ("Id");


--
-- Name: booking_computers PK_booking_computers; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.booking_computers
    ADD CONSTRAINT "PK_booking_computers" PRIMARY KEY ("Id");


--
-- Name: bookings PK_bookings; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bookings
    ADD CONSTRAINT "PK_bookings" PRIMARY KEY ("Id");


--
-- Name: branches PK_branches; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.branches
    ADD CONSTRAINT "PK_branches" PRIMARY KEY ("Id");


--
-- Name: case_definitions PK_case_definitions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.case_definitions
    ADD CONSTRAINT "PK_case_definitions" PRIMARY KEY ("Id");


--
-- Name: case_key_ledger PK_case_key_ledger; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.case_key_ledger
    ADD CONSTRAINT "PK_case_key_ledger" PRIMARY KEY ("Id");


--
-- Name: case_openings PK_case_openings; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.case_openings
    ADD CONSTRAINT "PK_case_openings" PRIMARY KEY ("Id");


--
-- Name: case_prizes PK_case_prizes; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.case_prizes
    ADD CONSTRAINT "PK_case_prizes" PRIMARY KEY ("Id");


--
-- Name: case_user_rewards PK_case_user_rewards; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.case_user_rewards
    ADD CONSTRAINT "PK_case_user_rewards" PRIMARY KEY ("Id");


--
-- Name: cash_movements PK_cash_movements; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_movements
    ADD CONSTRAINT "PK_cash_movements" PRIMARY KEY ("Id");


--
-- Name: cash_registers PK_cash_registers; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_registers
    ADD CONSTRAINT "PK_cash_registers" PRIMARY KEY ("Id");


--
-- Name: cash_shifts PK_cash_shifts; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_shifts
    ADD CONSTRAINT "PK_cash_shifts" PRIMARY KEY ("Id");


--
-- Name: club_news_posts PK_club_news_posts; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.club_news_posts
    ADD CONSTRAINT "PK_club_news_posts" PRIMARY KEY ("Id");


--
-- Name: computer_commands PK_computer_commands; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.computer_commands
    ADD CONSTRAINT "PK_computer_commands" PRIMARY KEY ("Id");


--
-- Name: computer_heartbeats PK_computer_heartbeats; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.computer_heartbeats
    ADD CONSTRAINT "PK_computer_heartbeats" PRIMARY KEY ("Id");


--
-- Name: computers PK_computers; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.computers
    ADD CONSTRAINT "PK_computers" PRIMARY KEY ("Id");


--
-- Name: customer_balance_transactions PK_customer_balance_transactions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_balance_transactions
    ADD CONSTRAINT "PK_customer_balance_transactions" PRIMARY KEY ("Id");


--
-- Name: customer_packages PK_customer_packages; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_packages
    ADD CONSTRAINT "PK_customer_packages" PRIMARY KEY ("Id");


--
-- Name: customer_telegram_outreach PK_customer_telegram_outreach; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_telegram_outreach
    ADD CONSTRAINT "PK_customer_telegram_outreach" PRIMARY KEY ("Id");


--
-- Name: customer_time_bank_transactions PK_customer_time_bank_transactions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_time_bank_transactions
    ADD CONSTRAINT "PK_customer_time_bank_transactions" PRIMARY KEY ("Id");


--
-- Name: customer_zone_time_banks PK_customer_zone_time_banks; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_zone_time_banks
    ADD CONSTRAINT "PK_customer_zone_time_banks" PRIMARY KEY ("Id");


--
-- Name: customers PK_customers; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customers
    ADD CONSTRAINT "PK_customers" PRIMARY KEY ("Id");


--
-- Name: document_sequences PK_document_sequences; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.document_sequences
    ADD CONSTRAINT "PK_document_sequences" PRIMARY KEY ("Id");


--
-- Name: employee_credentials PK_employee_credentials; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.employee_credentials
    ADD CONSTRAINT "PK_employee_credentials" PRIMARY KEY ("Id");


--
-- Name: employee_roles PK_employee_roles; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.employee_roles
    ADD CONSTRAINT "PK_employee_roles" PRIMARY KEY ("EmployeeId", "RoleId");


--
-- Name: employees PK_employees; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.employees
    ADD CONSTRAINT "PK_employees" PRIMARY KEY ("Id");


--
-- Name: floor_map_elements PK_floor_map_elements; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.floor_map_elements
    ADD CONSTRAINT "PK_floor_map_elements" PRIMARY KEY ("Id");


--
-- Name: gaming_sessions PK_gaming_sessions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gaming_sessions
    ADD CONSTRAINT "PK_gaming_sessions" PRIMARY KEY ("Id");


--
-- Name: inventory_movements PK_inventory_movements; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_movements
    ADD CONSTRAINT "PK_inventory_movements" PRIMARY KEY ("Id");


--
-- Name: loyalty_levels PK_loyalty_levels; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.loyalty_levels
    ADD CONSTRAINT "PK_loyalty_levels" PRIMARY KEY ("Id");


--
-- Name: payments PK_payments; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT "PK_payments" PRIMARY KEY ("Id");


--
-- Name: payroll_accruals PK_payroll_accruals; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payroll_accruals
    ADD CONSTRAINT "PK_payroll_accruals" PRIMARY KEY ("Id");


--
-- Name: permissions PK_permissions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.permissions
    ADD CONSTRAINT "PK_permissions" PRIMARY KEY ("Id");


--
-- Name: product_categories PK_product_categories; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_categories
    ADD CONSTRAINT "PK_product_categories" PRIMARY KEY ("Id");


--
-- Name: products PK_products; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT "PK_products" PRIMARY KEY ("Id");


--
-- Name: receipt_items PK_receipt_items; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.receipt_items
    ADD CONSTRAINT "PK_receipt_items" PRIMARY KEY ("Id");


--
-- Name: receipts PK_receipts; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.receipts
    ADD CONSTRAINT "PK_receipts" PRIMARY KEY ("Id");


--
-- Name: role_permissions PK_role_permissions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.role_permissions
    ADD CONSTRAINT "PK_role_permissions" PRIMARY KEY ("RoleId", "PermissionId");


--
-- Name: roles PK_roles; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.roles
    ADD CONSTRAINT "PK_roles" PRIMARY KEY ("Id");


--
-- Name: session_history PK_session_history; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.session_history
    ADD CONSTRAINT "PK_session_history" PRIMARY KEY ("Id");


--
-- Name: software_apps PK_software_apps; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.software_apps
    ADD CONSTRAINT "PK_software_apps" PRIMARY KEY ("Id");


--
-- Name: staff_wiki_pages PK_staff_wiki_pages; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.staff_wiki_pages
    ADD CONSTRAINT "PK_staff_wiki_pages" PRIMARY KEY ("Id");


--
-- Name: tariffs PK_tariffs; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tariffs
    ADD CONSTRAINT "PK_tariffs" PRIMARY KEY ("Id");


--
-- Name: telegram_auth_tickets PK_telegram_auth_tickets; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.telegram_auth_tickets
    ADD CONSTRAINT "PK_telegram_auth_tickets" PRIMARY KEY ("Id");


--
-- Name: work_shift_notes PK_work_shift_notes; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.work_shift_notes
    ADD CONSTRAINT "PK_work_shift_notes" PRIMARY KEY ("Id");


--
-- Name: work_shifts PK_work_shifts; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.work_shifts
    ADD CONSTRAINT "PK_work_shifts" PRIMARY KEY ("Id");


--
-- Name: zones PK_zones; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.zones
    ADD CONSTRAINT "PK_zones" PRIMARY KEY ("Id");


--
-- Name: IX_app_settings_BranchId_Key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_app_settings_BranchId_Key" ON public.app_settings USING btree ("BranchId", "Key");


--
-- Name: IX_audit_logs_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_audit_logs_CreatedAt" ON public.audit_logs USING btree ("CreatedAt");


--
-- Name: IX_audit_logs_EntityType_EntityId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_audit_logs_EntityType_EntityId" ON public.audit_logs USING btree ("EntityType", "EntityId");


--
-- Name: IX_bar_order_items_BarOrderId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_bar_order_items_BarOrderId" ON public.bar_order_items USING btree ("BarOrderId");


--
-- Name: IX_bar_order_items_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_bar_order_items_ProductId" ON public.bar_order_items USING btree ("ProductId");


--
-- Name: IX_bar_orders_IdempotencyKey; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_bar_orders_IdempotencyKey" ON public.bar_orders USING btree ("IdempotencyKey");


--
-- Name: IX_bar_orders_Number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_bar_orders_Number" ON public.bar_orders USING btree ("Number");


--
-- Name: IX_bar_orders_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_bar_orders_Status" ON public.bar_orders USING btree ("Status");


--
-- Name: IX_booking_computers_BookingId_ComputerId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_booking_computers_BookingId_ComputerId" ON public.booking_computers USING btree ("BookingId", "ComputerId");


--
-- Name: IX_booking_computers_ComputerId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_booking_computers_ComputerId" ON public.booking_computers USING btree ("ComputerId");


--
-- Name: IX_bookings_BranchId_StartsAt_EndsAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_bookings_BranchId_StartsAt_EndsAt" ON public.bookings USING btree ("BranchId", "StartsAt", "EndsAt");


--
-- Name: IX_bookings_CustomerId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_bookings_CustomerId" ON public.bookings USING btree ("CustomerId");


--
-- Name: IX_bookings_IdempotencyKey; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_bookings_IdempotencyKey" ON public.bookings USING btree ("IdempotencyKey");


--
-- Name: IX_bookings_Number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_bookings_Number" ON public.bookings USING btree ("Number");


--
-- Name: IX_bookings_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_bookings_Status" ON public.bookings USING btree ("Status");


--
-- Name: IX_bookings_ZoneId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_bookings_ZoneId" ON public.bookings USING btree ("ZoneId");


--
-- Name: IX_branches_Code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_branches_Code" ON public.branches USING btree ("Code");


--
-- Name: IX_case_definitions_Code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_case_definitions_Code" ON public.case_definitions USING btree ("Code");


--
-- Name: IX_case_key_ledger_CustomerId_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_case_key_ledger_CustomerId_CreatedAt" ON public.case_key_ledger USING btree ("CustomerId", "CreatedAt");


--
-- Name: IX_case_key_ledger_IdempotencyKey; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_case_key_ledger_IdempotencyKey" ON public.case_key_ledger USING btree ("IdempotencyKey") WHERE ("IdempotencyKey" IS NOT NULL);


--
-- Name: IX_case_openings_CaseDefinitionId_ClubDayKey_CasePrizeId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_case_openings_CaseDefinitionId_ClubDayKey_CasePrizeId" ON public.case_openings USING btree ("CaseDefinitionId", "ClubDayKey", "CasePrizeId");


--
-- Name: IX_case_openings_CasePrizeId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_case_openings_CasePrizeId" ON public.case_openings USING btree ("CasePrizeId");


--
-- Name: IX_case_openings_CustomerId_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_case_openings_CustomerId_CreatedAt" ON public.case_openings USING btree ("CustomerId", "CreatedAt");


--
-- Name: IX_case_openings_IdempotencyKey; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_case_openings_IdempotencyKey" ON public.case_openings USING btree ("IdempotencyKey") WHERE ("IdempotencyKey" IS NOT NULL);


--
-- Name: IX_case_prizes_CaseDefinitionId_PrizeCode; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_case_prizes_CaseDefinitionId_PrizeCode" ON public.case_prizes USING btree ("CaseDefinitionId", "PrizeCode");


--
-- Name: IX_case_user_rewards_CaseOpeningId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_case_user_rewards_CaseOpeningId" ON public.case_user_rewards USING btree ("CaseOpeningId");


--
-- Name: IX_case_user_rewards_CustomerId_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_case_user_rewards_CustomerId_Status" ON public.case_user_rewards USING btree ("CustomerId", "Status");


--
-- Name: IX_case_user_rewards_Status_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_case_user_rewards_Status_CreatedAt" ON public.case_user_rewards USING btree ("Status", "CreatedAt");


--
-- Name: IX_cash_movements_CashShiftId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_cash_movements_CashShiftId" ON public.cash_movements USING btree ("CashShiftId");


--
-- Name: IX_cash_registers_BranchId_Code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_cash_registers_BranchId_Code" ON public.cash_registers USING btree ("BranchId", "Code");


--
-- Name: IX_cash_shifts_CashRegisterId_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_cash_shifts_CashRegisterId_Status" ON public.cash_shifts USING btree ("CashRegisterId", "Status");


--
-- Name: IX_cash_shifts_Number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_cash_shifts_Number" ON public.cash_shifts USING btree ("Number");


--
-- Name: IX_club_news_posts_BranchId_IsPublished_IsPinned_SortOrder; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_club_news_posts_BranchId_IsPublished_IsPinned_SortOrder" ON public.club_news_posts USING btree ("BranchId", "IsPublished", "IsPinned", "SortOrder");


--
-- Name: IX_computer_commands_ComputerId_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_computer_commands_ComputerId_Status" ON public.computer_commands USING btree ("ComputerId", "Status");


--
-- Name: IX_computer_commands_IdempotencyKey; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_computer_commands_IdempotencyKey" ON public.computer_commands USING btree ("IdempotencyKey");


--
-- Name: IX_computer_heartbeats_ComputerId_ReceivedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_computer_heartbeats_ComputerId_ReceivedAt" ON public.computer_heartbeats USING btree ("ComputerId", "ReceivedAt");


--
-- Name: IX_computers_BranchId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_computers_BranchId" ON public.computers USING btree ("BranchId");


--
-- Name: IX_computers_CurrentSessionId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_computers_CurrentSessionId" ON public.computers USING btree ("CurrentSessionId");


--
-- Name: IX_computers_InstallationId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_computers_InstallationId" ON public.computers USING btree ("InstallationId");


--
-- Name: IX_computers_MacAddress; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_computers_MacAddress" ON public.computers USING btree ("MacAddress");


--
-- Name: IX_computers_RegistrationCode; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_computers_RegistrationCode" ON public.computers USING btree ("RegistrationCode");


--
-- Name: IX_computers_StationKind; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_computers_StationKind" ON public.computers USING btree ("StationKind");


--
-- Name: IX_computers_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_computers_Status" ON public.computers USING btree ("Status");


--
-- Name: IX_computers_ZoneId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_computers_ZoneId" ON public.computers USING btree ("ZoneId");


--
-- Name: IX_customer_balance_transactions_CustomerId_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_customer_balance_transactions_CustomerId_CreatedAt" ON public.customer_balance_transactions USING btree ("CustomerId", "CreatedAt");


--
-- Name: IX_customer_balance_transactions_IdempotencyKey; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_customer_balance_transactions_IdempotencyKey" ON public.customer_balance_transactions USING btree ("IdempotencyKey");


--
-- Name: IX_customer_packages_CustomerId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_customer_packages_CustomerId" ON public.customer_packages USING btree ("CustomerId");


--
-- Name: IX_customer_telegram_outreach_CustomerId_SentAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_customer_telegram_outreach_CustomerId_SentAt" ON public.customer_telegram_outreach USING btree ("CustomerId", "SentAt");


--
-- Name: IX_customer_telegram_outreach_Kind_SentAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_customer_telegram_outreach_Kind_SentAt" ON public.customer_telegram_outreach USING btree ("Kind", "SentAt");


--
-- Name: IX_customer_time_bank_transactions_CustomerId_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_customer_time_bank_transactions_CustomerId_CreatedAt" ON public.customer_time_bank_transactions USING btree ("CustomerId", "CreatedAt");


--
-- Name: IX_customer_time_bank_transactions_CustomerId_ZoneId_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_customer_time_bank_transactions_CustomerId_ZoneId_CreatedAt" ON public.customer_time_bank_transactions USING btree ("CustomerId", "ZoneId", "CreatedAt");


--
-- Name: IX_customer_time_bank_transactions_IdempotencyKey; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_customer_time_bank_transactions_IdempotencyKey" ON public.customer_time_bank_transactions USING btree ("IdempotencyKey");


--
-- Name: IX_customer_time_bank_transactions_ZoneId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_customer_time_bank_transactions_ZoneId" ON public.customer_time_bank_transactions USING btree ("ZoneId");


--
-- Name: IX_customer_zone_time_banks_CustomerId_ZoneId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_customer_zone_time_banks_CustomerId_ZoneId" ON public.customer_zone_time_banks USING btree ("CustomerId", "ZoneId");


--
-- Name: IX_customer_zone_time_banks_ZoneId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_customer_zone_time_banks_ZoneId" ON public.customer_zone_time_banks USING btree ("ZoneId");


--
-- Name: IX_customers_BranchId_Iin; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_customers_BranchId_Iin" ON public.customers USING btree ("BranchId", "Iin") WHERE ("Iin" IS NOT NULL);


--
-- Name: IX_customers_BranchId_Phone; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_customers_BranchId_Phone" ON public.customers USING btree ("BranchId", "Phone");


--
-- Name: IX_customers_LastName; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_customers_LastName" ON public.customers USING btree ("LastName");


--
-- Name: IX_customers_LoggedInComputerId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_customers_LoggedInComputerId" ON public.customers USING btree ("LoggedInComputerId");


--
-- Name: IX_customers_LoyaltyLevelId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_customers_LoyaltyLevelId" ON public.customers USING btree ("LoyaltyLevelId");


--
-- Name: IX_customers_TelegramUserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_customers_TelegramUserId" ON public.customers USING btree ("TelegramUserId");


--
-- Name: IX_document_sequences_BranchId_Type_Year; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_document_sequences_BranchId_Type_Year" ON public.document_sequences USING btree ("BranchId", "Type", "Year");


--
-- Name: IX_employee_credentials_EmployeeId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_employee_credentials_EmployeeId" ON public.employee_credentials USING btree ("EmployeeId");


--
-- Name: IX_employee_roles_RoleId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_employee_roles_RoleId" ON public.employee_roles USING btree ("RoleId");


--
-- Name: IX_employees_BranchId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_employees_BranchId" ON public.employees USING btree ("BranchId");


--
-- Name: IX_employees_Iin; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_employees_Iin" ON public.employees USING btree ("Iin") WHERE ("Iin" IS NOT NULL);


--
-- Name: IX_employees_Login; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_employees_Login" ON public.employees USING btree ("Login");


--
-- Name: IX_employees_TelegramUserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_employees_TelegramUserId" ON public.employees USING btree ("TelegramUserId");


--
-- Name: IX_floor_map_elements_BranchId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_floor_map_elements_BranchId" ON public.floor_map_elements USING btree ("BranchId");


--
-- Name: IX_gaming_sessions_BranchId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_gaming_sessions_BranchId" ON public.gaming_sessions USING btree ("BranchId");


--
-- Name: IX_gaming_sessions_ComputerId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_gaming_sessions_ComputerId" ON public.gaming_sessions USING btree ("ComputerId");


--
-- Name: IX_gaming_sessions_ComputerId_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_gaming_sessions_ComputerId_Status" ON public.gaming_sessions USING btree ("ComputerId", "Status");


--
-- Name: IX_gaming_sessions_IdempotencyKey; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_gaming_sessions_IdempotencyKey" ON public.gaming_sessions USING btree ("IdempotencyKey");


--
-- Name: IX_gaming_sessions_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_gaming_sessions_Status" ON public.gaming_sessions USING btree ("Status");


--
-- Name: IX_gaming_sessions_TariffId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_gaming_sessions_TariffId" ON public.gaming_sessions USING btree ("TariffId");


--
-- Name: IX_gaming_sessions_ZoneId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_gaming_sessions_ZoneId" ON public.gaming_sessions USING btree ("ZoneId");


--
-- Name: IX_inventory_movements_IdempotencyKey; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_inventory_movements_IdempotencyKey" ON public.inventory_movements USING btree ("IdempotencyKey");


--
-- Name: IX_inventory_movements_ProductId_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_inventory_movements_ProductId_CreatedAt" ON public.inventory_movements USING btree ("ProductId", "CreatedAt");


--
-- Name: IX_loyalty_levels_BranchId_Code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_loyalty_levels_BranchId_Code" ON public.loyalty_levels USING btree ("BranchId", "Code");


--
-- Name: IX_payments_ReceiptId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_payments_ReceiptId" ON public.payments USING btree ("ReceiptId");


--
-- Name: IX_payroll_accruals_EmployeeId_PeriodFrom_PeriodTo; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_payroll_accruals_EmployeeId_PeriodFrom_PeriodTo" ON public.payroll_accruals USING btree ("EmployeeId", "PeriodFrom", "PeriodTo");


--
-- Name: IX_permissions_Code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_permissions_Code" ON public.permissions USING btree ("Code");


--
-- Name: IX_product_categories_BranchId_Code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_product_categories_BranchId_Code" ON public.product_categories USING btree ("BranchId", "Code");


--
-- Name: IX_products_BranchId_Sku; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_products_BranchId_Sku" ON public.products USING btree ("BranchId", "Sku");


--
-- Name: IX_products_CategoryId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_products_CategoryId" ON public.products USING btree ("CategoryId");


--
-- Name: IX_receipt_items_ReceiptId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_receipt_items_ReceiptId" ON public.receipt_items USING btree ("ReceiptId");


--
-- Name: IX_receipts_CashShiftId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_receipts_CashShiftId" ON public.receipts USING btree ("CashShiftId");


--
-- Name: IX_receipts_IdempotencyKey; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_receipts_IdempotencyKey" ON public.receipts USING btree ("IdempotencyKey");


--
-- Name: IX_receipts_Number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_receipts_Number" ON public.receipts USING btree ("Number");


--
-- Name: IX_role_permissions_PermissionId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_role_permissions_PermissionId" ON public.role_permissions USING btree ("PermissionId");


--
-- Name: IX_roles_Code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_roles_Code" ON public.roles USING btree ("Code");


--
-- Name: IX_session_history_SessionId_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_session_history_SessionId_CreatedAt" ON public.session_history USING btree ("SessionId", "CreatedAt");


--
-- Name: IX_software_apps_BranchId_SortOrder; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_software_apps_BranchId_SortOrder" ON public.software_apps USING btree ("BranchId", "SortOrder");


--
-- Name: IX_staff_wiki_pages_Slug; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_staff_wiki_pages_Slug" ON public.staff_wiki_pages USING btree ("Slug");


--
-- Name: IX_staff_wiki_pages_SortOrder; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_staff_wiki_pages_SortOrder" ON public.staff_wiki_pages USING btree ("SortOrder");


--
-- Name: IX_tariffs_BranchId_Code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_tariffs_BranchId_Code" ON public.tariffs USING btree ("BranchId", "Code");


--
-- Name: IX_tariffs_ZoneId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_tariffs_ZoneId" ON public.tariffs USING btree ("ZoneId");


--
-- Name: IX_telegram_auth_tickets_Code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_telegram_auth_tickets_Code" ON public.telegram_auth_tickets USING btree ("Code");


--
-- Name: IX_telegram_auth_tickets_ComputerId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_telegram_auth_tickets_ComputerId" ON public.telegram_auth_tickets USING btree ("ComputerId");


--
-- Name: IX_telegram_auth_tickets_Purpose_ClientNonce_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_telegram_auth_tickets_Purpose_ClientNonce_Status" ON public.telegram_auth_tickets USING btree ("Purpose", "ClientNonce", "Status");


--
-- Name: IX_telegram_auth_tickets_Status_ExpiresAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_telegram_auth_tickets_Status_ExpiresAt" ON public.telegram_auth_tickets USING btree ("Status", "ExpiresAt");


--
-- Name: IX_work_shift_notes_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_work_shift_notes_CreatedAt" ON public.work_shift_notes USING btree ("CreatedAt");


--
-- Name: IX_work_shift_notes_WorkShiftId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_work_shift_notes_WorkShiftId" ON public.work_shift_notes USING btree ("WorkShiftId");


--
-- Name: IX_work_shifts_BranchId_WorkDate; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_work_shifts_BranchId_WorkDate" ON public.work_shifts USING btree ("BranchId", "WorkDate");


--
-- Name: IX_work_shifts_EmployeeId_WorkDate; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_work_shifts_EmployeeId_WorkDate" ON public.work_shifts USING btree ("EmployeeId", "WorkDate");


--
-- Name: IX_zones_BranchId_Code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_zones_BranchId_Code" ON public.zones USING btree ("BranchId", "Code");


--
-- Name: ix_gaming_sessions_one_live_per_computer; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_gaming_sessions_one_live_per_computer ON public.gaming_sessions USING btree ("ComputerId") WHERE (("Status")::text = ANY ((ARRAY['Active'::character varying, 'Paused'::character varying])::text[]));


--
-- Name: bar_order_items FK_bar_order_items_bar_orders_BarOrderId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bar_order_items
    ADD CONSTRAINT "FK_bar_order_items_bar_orders_BarOrderId" FOREIGN KEY ("BarOrderId") REFERENCES public.bar_orders("Id") ON DELETE CASCADE;


--
-- Name: bar_order_items FK_bar_order_items_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bar_order_items
    ADD CONSTRAINT "FK_bar_order_items_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE RESTRICT;


--
-- Name: booking_computers FK_booking_computers_bookings_BookingId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.booking_computers
    ADD CONSTRAINT "FK_booking_computers_bookings_BookingId" FOREIGN KEY ("BookingId") REFERENCES public.bookings("Id") ON DELETE CASCADE;


--
-- Name: booking_computers FK_booking_computers_computers_ComputerId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.booking_computers
    ADD CONSTRAINT "FK_booking_computers_computers_ComputerId" FOREIGN KEY ("ComputerId") REFERENCES public.computers("Id") ON DELETE RESTRICT;


--
-- Name: bookings FK_bookings_branches_BranchId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bookings
    ADD CONSTRAINT "FK_bookings_branches_BranchId" FOREIGN KEY ("BranchId") REFERENCES public.branches("Id") ON DELETE RESTRICT;


--
-- Name: bookings FK_bookings_customers_CustomerId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bookings
    ADD CONSTRAINT "FK_bookings_customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES public.customers("Id") ON DELETE SET NULL;


--
-- Name: bookings FK_bookings_zones_ZoneId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bookings
    ADD CONSTRAINT "FK_bookings_zones_ZoneId" FOREIGN KEY ("ZoneId") REFERENCES public.zones("Id") ON DELETE SET NULL;


--
-- Name: case_key_ledger FK_case_key_ledger_customers_CustomerId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.case_key_ledger
    ADD CONSTRAINT "FK_case_key_ledger_customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES public.customers("Id") ON DELETE CASCADE;


--
-- Name: case_openings FK_case_openings_case_definitions_CaseDefinitionId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.case_openings
    ADD CONSTRAINT "FK_case_openings_case_definitions_CaseDefinitionId" FOREIGN KEY ("CaseDefinitionId") REFERENCES public.case_definitions("Id") ON DELETE RESTRICT;


--
-- Name: case_openings FK_case_openings_case_prizes_CasePrizeId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.case_openings
    ADD CONSTRAINT "FK_case_openings_case_prizes_CasePrizeId" FOREIGN KEY ("CasePrizeId") REFERENCES public.case_prizes("Id") ON DELETE RESTRICT;


--
-- Name: case_openings FK_case_openings_customers_CustomerId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.case_openings
    ADD CONSTRAINT "FK_case_openings_customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES public.customers("Id") ON DELETE CASCADE;


--
-- Name: case_prizes FK_case_prizes_case_definitions_CaseDefinitionId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.case_prizes
    ADD CONSTRAINT "FK_case_prizes_case_definitions_CaseDefinitionId" FOREIGN KEY ("CaseDefinitionId") REFERENCES public.case_definitions("Id") ON DELETE CASCADE;


--
-- Name: case_user_rewards FK_case_user_rewards_case_openings_CaseOpeningId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.case_user_rewards
    ADD CONSTRAINT "FK_case_user_rewards_case_openings_CaseOpeningId" FOREIGN KEY ("CaseOpeningId") REFERENCES public.case_openings("Id") ON DELETE CASCADE;


--
-- Name: case_user_rewards FK_case_user_rewards_customers_CustomerId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.case_user_rewards
    ADD CONSTRAINT "FK_case_user_rewards_customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES public.customers("Id") ON DELETE CASCADE;


--
-- Name: cash_movements FK_cash_movements_cash_shifts_CashShiftId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_movements
    ADD CONSTRAINT "FK_cash_movements_cash_shifts_CashShiftId" FOREIGN KEY ("CashShiftId") REFERENCES public.cash_shifts("Id") ON DELETE CASCADE;


--
-- Name: cash_registers FK_cash_registers_branches_BranchId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_registers
    ADD CONSTRAINT "FK_cash_registers_branches_BranchId" FOREIGN KEY ("BranchId") REFERENCES public.branches("Id") ON DELETE RESTRICT;


--
-- Name: cash_shifts FK_cash_shifts_cash_registers_CashRegisterId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cash_shifts
    ADD CONSTRAINT "FK_cash_shifts_cash_registers_CashRegisterId" FOREIGN KEY ("CashRegisterId") REFERENCES public.cash_registers("Id") ON DELETE RESTRICT;


--
-- Name: computer_commands FK_computer_commands_computers_ComputerId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.computer_commands
    ADD CONSTRAINT "FK_computer_commands_computers_ComputerId" FOREIGN KEY ("ComputerId") REFERENCES public.computers("Id") ON DELETE CASCADE;


--
-- Name: computer_heartbeats FK_computer_heartbeats_computers_ComputerId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.computer_heartbeats
    ADD CONSTRAINT "FK_computer_heartbeats_computers_ComputerId" FOREIGN KEY ("ComputerId") REFERENCES public.computers("Id") ON DELETE CASCADE;


--
-- Name: computers FK_computers_branches_BranchId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.computers
    ADD CONSTRAINT "FK_computers_branches_BranchId" FOREIGN KEY ("BranchId") REFERENCES public.branches("Id") ON DELETE RESTRICT;


--
-- Name: computers FK_computers_gaming_sessions_CurrentSessionId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.computers
    ADD CONSTRAINT "FK_computers_gaming_sessions_CurrentSessionId" FOREIGN KEY ("CurrentSessionId") REFERENCES public.gaming_sessions("Id") ON DELETE SET NULL;


--
-- Name: computers FK_computers_zones_ZoneId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.computers
    ADD CONSTRAINT "FK_computers_zones_ZoneId" FOREIGN KEY ("ZoneId") REFERENCES public.zones("Id") ON DELETE SET NULL;


--
-- Name: customer_balance_transactions FK_customer_balance_transactions_customers_CustomerId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_balance_transactions
    ADD CONSTRAINT "FK_customer_balance_transactions_customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES public.customers("Id") ON DELETE RESTRICT;


--
-- Name: customer_packages FK_customer_packages_customers_CustomerId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_packages
    ADD CONSTRAINT "FK_customer_packages_customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES public.customers("Id") ON DELETE CASCADE;


--
-- Name: customer_telegram_outreach FK_customer_telegram_outreach_customers_CustomerId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_telegram_outreach
    ADD CONSTRAINT "FK_customer_telegram_outreach_customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES public.customers("Id") ON DELETE CASCADE;


--
-- Name: customer_time_bank_transactions FK_customer_time_bank_transactions_customers_CustomerId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_time_bank_transactions
    ADD CONSTRAINT "FK_customer_time_bank_transactions_customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES public.customers("Id") ON DELETE RESTRICT;


--
-- Name: customer_time_bank_transactions FK_customer_time_bank_transactions_zones_ZoneId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_time_bank_transactions
    ADD CONSTRAINT "FK_customer_time_bank_transactions_zones_ZoneId" FOREIGN KEY ("ZoneId") REFERENCES public.zones("Id") ON DELETE RESTRICT;


--
-- Name: customer_zone_time_banks FK_customer_zone_time_banks_customers_CustomerId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_zone_time_banks
    ADD CONSTRAINT "FK_customer_zone_time_banks_customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES public.customers("Id") ON DELETE CASCADE;


--
-- Name: customer_zone_time_banks FK_customer_zone_time_banks_zones_ZoneId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_zone_time_banks
    ADD CONSTRAINT "FK_customer_zone_time_banks_zones_ZoneId" FOREIGN KEY ("ZoneId") REFERENCES public.zones("Id") ON DELETE RESTRICT;


--
-- Name: customers FK_customers_loyalty_levels_LoyaltyLevelId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customers
    ADD CONSTRAINT "FK_customers_loyalty_levels_LoyaltyLevelId" FOREIGN KEY ("LoyaltyLevelId") REFERENCES public.loyalty_levels("Id") ON DELETE SET NULL;


--
-- Name: employee_credentials FK_employee_credentials_employees_EmployeeId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.employee_credentials
    ADD CONSTRAINT "FK_employee_credentials_employees_EmployeeId" FOREIGN KEY ("EmployeeId") REFERENCES public.employees("Id") ON DELETE CASCADE;


--
-- Name: employee_roles FK_employee_roles_employees_EmployeeId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.employee_roles
    ADD CONSTRAINT "FK_employee_roles_employees_EmployeeId" FOREIGN KEY ("EmployeeId") REFERENCES public.employees("Id") ON DELETE CASCADE;


--
-- Name: employee_roles FK_employee_roles_roles_RoleId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.employee_roles
    ADD CONSTRAINT "FK_employee_roles_roles_RoleId" FOREIGN KEY ("RoleId") REFERENCES public.roles("Id") ON DELETE CASCADE;


--
-- Name: employees FK_employees_branches_BranchId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.employees
    ADD CONSTRAINT "FK_employees_branches_BranchId" FOREIGN KEY ("BranchId") REFERENCES public.branches("Id") ON DELETE SET NULL;


--
-- Name: floor_map_elements FK_floor_map_elements_branches_BranchId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.floor_map_elements
    ADD CONSTRAINT "FK_floor_map_elements_branches_BranchId" FOREIGN KEY ("BranchId") REFERENCES public.branches("Id") ON DELETE CASCADE;


--
-- Name: gaming_sessions FK_gaming_sessions_branches_BranchId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gaming_sessions
    ADD CONSTRAINT "FK_gaming_sessions_branches_BranchId" FOREIGN KEY ("BranchId") REFERENCES public.branches("Id") ON DELETE RESTRICT;


--
-- Name: gaming_sessions FK_gaming_sessions_computers_ComputerId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gaming_sessions
    ADD CONSTRAINT "FK_gaming_sessions_computers_ComputerId" FOREIGN KEY ("ComputerId") REFERENCES public.computers("Id") ON DELETE RESTRICT;


--
-- Name: gaming_sessions FK_gaming_sessions_tariffs_TariffId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gaming_sessions
    ADD CONSTRAINT "FK_gaming_sessions_tariffs_TariffId" FOREIGN KEY ("TariffId") REFERENCES public.tariffs("Id") ON DELETE RESTRICT;


--
-- Name: gaming_sessions FK_gaming_sessions_zones_ZoneId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gaming_sessions
    ADD CONSTRAINT "FK_gaming_sessions_zones_ZoneId" FOREIGN KEY ("ZoneId") REFERENCES public.zones("Id") ON DELETE RESTRICT;


--
-- Name: inventory_movements FK_inventory_movements_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_movements
    ADD CONSTRAINT "FK_inventory_movements_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE RESTRICT;


--
-- Name: payments FK_payments_receipts_ReceiptId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT "FK_payments_receipts_ReceiptId" FOREIGN KEY ("ReceiptId") REFERENCES public.receipts("Id") ON DELETE CASCADE;


--
-- Name: payroll_accruals FK_payroll_accruals_employees_EmployeeId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payroll_accruals
    ADD CONSTRAINT "FK_payroll_accruals_employees_EmployeeId" FOREIGN KEY ("EmployeeId") REFERENCES public.employees("Id") ON DELETE RESTRICT;


--
-- Name: products FK_products_product_categories_CategoryId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT "FK_products_product_categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES public.product_categories("Id") ON DELETE RESTRICT;


--
-- Name: receipt_items FK_receipt_items_receipts_ReceiptId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.receipt_items
    ADD CONSTRAINT "FK_receipt_items_receipts_ReceiptId" FOREIGN KEY ("ReceiptId") REFERENCES public.receipts("Id") ON DELETE CASCADE;


--
-- Name: receipts FK_receipts_cash_shifts_CashShiftId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.receipts
    ADD CONSTRAINT "FK_receipts_cash_shifts_CashShiftId" FOREIGN KEY ("CashShiftId") REFERENCES public.cash_shifts("Id") ON DELETE RESTRICT;


--
-- Name: role_permissions FK_role_permissions_permissions_PermissionId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.role_permissions
    ADD CONSTRAINT "FK_role_permissions_permissions_PermissionId" FOREIGN KEY ("PermissionId") REFERENCES public.permissions("Id") ON DELETE CASCADE;


--
-- Name: role_permissions FK_role_permissions_roles_RoleId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.role_permissions
    ADD CONSTRAINT "FK_role_permissions_roles_RoleId" FOREIGN KEY ("RoleId") REFERENCES public.roles("Id") ON DELETE CASCADE;


--
-- Name: session_history FK_session_history_gaming_sessions_SessionId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.session_history
    ADD CONSTRAINT "FK_session_history_gaming_sessions_SessionId" FOREIGN KEY ("SessionId") REFERENCES public.gaming_sessions("Id") ON DELETE CASCADE;


--
-- Name: tariffs FK_tariffs_branches_BranchId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tariffs
    ADD CONSTRAINT "FK_tariffs_branches_BranchId" FOREIGN KEY ("BranchId") REFERENCES public.branches("Id") ON DELETE RESTRICT;


--
-- Name: tariffs FK_tariffs_zones_ZoneId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tariffs
    ADD CONSTRAINT "FK_tariffs_zones_ZoneId" FOREIGN KEY ("ZoneId") REFERENCES public.zones("Id") ON DELETE SET NULL;


--
-- Name: work_shift_notes FK_work_shift_notes_work_shifts_WorkShiftId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.work_shift_notes
    ADD CONSTRAINT "FK_work_shift_notes_work_shifts_WorkShiftId" FOREIGN KEY ("WorkShiftId") REFERENCES public.work_shifts("Id") ON DELETE CASCADE;


--
-- Name: work_shifts FK_work_shifts_employees_EmployeeId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.work_shifts
    ADD CONSTRAINT "FK_work_shifts_employees_EmployeeId" FOREIGN KEY ("EmployeeId") REFERENCES public.employees("Id") ON DELETE RESTRICT;


--
-- Name: zones FK_zones_branches_BranchId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.zones
    ADD CONSTRAINT "FK_zones_branches_BranchId" FOREIGN KEY ("BranchId") REFERENCES public.branches("Id") ON DELETE RESTRICT;


--
-- PostgreSQL database dump complete
--

\unrestrict wnn44KOcLifb0JhuHn5Yeyd96Y5vGCIbkKoap4fr5Pzqk20GQ8W38rI0smdDbes

