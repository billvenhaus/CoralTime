﻿using AutoMapper;
using CoralTime.BL.Interfaces.Reports;
using CoralTime.Common.Constants;
using CoralTime.DAL.Repositories;
using CoralTime.ViewModels.Reports.Request.Grid;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Net.Http.Headers;
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using CoralTime.ViewModels.Reports.Responce.Grid.ReportTotal;
using CoralTime.Common.Helpers;

namespace CoralTime.BL.Services.Reports.Export
{
    public partial class ReportsExportService : BaseService, IReportExportService
    {
        private IWebHostEnvironment _environment { get; }
        private readonly IConfiguration _configuration;
        private readonly IReportsService _reportService;

        public ReportsExportService(UnitOfWork uow, IMapper mapper, IConfiguration configuration, IWebHostEnvironment environment, IReportsService reportService)
            : base(uow, mapper)
        {
            _configuration = configuration;
            _environment = environment;
            _reportService = reportService;
        }

        #region Constants.

        private const string ExtensionXLSX = ".xlsx";
        private const string ExtensionCSV = ".csv";
        private const string ExtensionPDF = ".pdf";

        private const string ContentTypeXLSX = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        //private const string ContentTypeXLSX = "application/octet-stream";
        private const string ContentTypeCSV = "application/csv";
        private const string ContentTypePDF = "application/pdf";

        #endregion

        #region Properties. 

        private string FileName { get; set; } = Constants.CoralTime;

        private string ContentType { get; set; } = string.Empty;

        #endregion

        #region Export Excel, CSV, PDF. 

        public async Task<FileResult> ExportFileReportsGridAsync(ReportsGridView reportsGridData, HttpContext httpContext)
        {
            var groupByType = _reportService.GetReportsGrid(reportsGridData);

            var fileOfBytes = await CreateFileOfBytesReportsGridAsync(reportsGridData, groupByType);
            var fileStreamResult = SaveFileOfBytesToFileStreamResult(httpContext, fileOfBytes);

            return fileStreamResult;
        }

        #endregion

        #region Export Excel, CSV, PDF. (Common methods)

        private async Task<byte[]> CreateFileOfBytesReportsGridAsync(ReportsGridView reportsGridView, ReportTotalView reportTotalView)
        {
            var fileOfBytes = new byte[0];

            // remove markdown
            foreach (var groupedItem in reportTotalView.GroupedItems)
            {
                foreach (var item in groupedItem.Items)
                {
                    if (!string.IsNullOrWhiteSpace(item.Notes))
                    {
                        item.Notes = StringHandler.RemoveMarkdown(item.Notes);
                    }
                }
            }

            // TODO Check!
            UpdateFileName((DateTime) reportsGridView.CurrentQuery.DateFrom, (DateTime) reportsGridView.CurrentQuery.DateTo);

            switch (reportsGridView.FileTypeId ?? 0)
            {
                case (int) Constants.FileType.Excel:
                {
                    FileName = FileName + ExtensionXLSX;
                    fileOfBytes = CreateFileExcel(reportTotalView);
                    ContentType = ContentTypeXLSX;

                    break;
                }

                case (int) Constants.FileType.Csv:
                {
                    FileName = FileName + ExtensionCSV;
                    //file = CreateFileCSV(reportTotalView);
                    ContentType = ContentTypeCSV;

                    break;
                }

                case (int) Constants.FileType.Pdf:
                {
                    FileName = FileName + ExtensionPDF;
                    fileOfBytes = await CreateFilePDFAsync(reportTotalView);
                    ContentType = ContentTypePDF;

                    break;
                }
            }

            return fileOfBytes;
        }

        private FileStreamResult SaveFileOfBytesToFileStreamResult(HttpContext httpContext, byte[] fileByte)
        {
            httpContext.Response.ContentType = ContentType;

            var fileStreamResult = new FileStreamResult(new MemoryStream(fileByte), new MediaTypeHeaderValue(ContentType))
            {
                FileDownloadName = FileName
            };

            return fileStreamResult;
        }
        
        private void UpdateFileName(DateTime dateFrom, DateTime dateTo)
        {
            if (!string.IsNullOrEmpty(_reportService.SingleFilteredProjectName))
            {
                FileName = FileName + " " + _reportService.SingleFilteredProjectName + " " + GetAbbreviatedMonthName(dateFrom) + " - " + GetAbbreviatedMonthName(dateTo);
            }
            else
            {
                FileName = FileName + " Reports " + GetAbbreviatedMonthName(dateFrom) + " - " + GetAbbreviatedMonthName(dateTo);
            }
        }

        private string GetAbbreviatedMonthName(DateTime date)
        {
            return CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(date.Month) + " " + date.Day;
        }

        #endregion
    }
}